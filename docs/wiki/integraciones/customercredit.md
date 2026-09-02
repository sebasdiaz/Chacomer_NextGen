<!-- wiki-meta
sources:
  - src/integrations/customers/AxxonCustomerCredit.Functions/**
  - pipelines/azure-pipelines-customercredit.yml
last_reviewed: 2026-09-02
-->

# Customer Credit — créditos de clientes desde F&O

Azure Function (.NET 10 isolated) que expone **cuatro endpoints HTTP de solo lectura**
sobre las entidades de crédito de Finance & Operations, para que las consuman aplicaciones
satélite.

No consume Service Bus, no escribe nada y **no habla con Dataverse**: los datos de crédito
viven sólo en el ERP. Es la contracara de lectura de [Customers](customers.md), igual que
[Customer Data](customerdata.md) lo es sobre Dataverse.

> **Estado: sin desplegar.** El código, el Bicep y el pipeline están; la Function App
> todavía no existe en Azure y los stages de deploy están apagados. Probado end-to-end
> sólo desde una máquina de desarrollo contra F&O INTE.
> Ver [Cómo se estrena](#cómo-se-estrena).

## Las cuatro entidades

Son entidades **custom** de F&O (prefijo `Dev`), verificadas contra el `$metadata` de INTE
el 2026-09-02.

| Entidad | Qué es | Clave |
|---|---|---|
| `DevAxCustCreditCustomers` | Ficha crediticia del cliente: datos personales, laborales, ingresos y marcas de riesgo | `dataAreaId` + `CustomerAccount` |
| `DevAxCustCreditGrantedPlans` | Plan de crédito otorgado (cabecera) | `dataAreaId` + `CreditId` |
| `DevAxCustCreditInstallments` | Cuotas de un plan | `dataAreaId` + `CreditId` + `InstallmentNumber` |
| `DevAxCustCreditResolutions` | Resolución de un analista sobre una solicitud | `dataAreaId` + `SolicitudId` + `ResolutionId` |

Cómo se encadenan, que es lo que no se ve mirando una tabla sola:

```
DevAxCustCreditCustomers  ── CustomerAccount ──┐
                                               │
DevAxCustCreditGrantedPlans ───────────────────┤  CustomerAccount
   │  CreditId                                 │
   └─► DevAxCustCreditInstallments ────────────┘  (denormalizado en la cuota)
   │
   │  RequestId  ==  SolicitudId
   └─► DevAxCustCreditResolutions
```

Dos cosas de ese diagrama que importan al escribir un consumidor:

- **La cuota trae `CustomerAccount` denormalizado.** Por eso se pueden pedir todas las
  cuotas de un cliente sin resolver antes sus planes.
- **La resolución NO tiene `CustomerAccount`.** Se llega a ella por `SolicitudId`, que es
  el `RequestId` del plan. Un satélite que quiera "las resoluciones de un cliente" tiene
  que traer primero sus planes y después consultar por cada `RequestId`. No hay atajo:
  el campo no existe en la entidad.

Hay más entidades en la misma familia (`DevAxCustCreditPlanRequests`,
`DevAxCustCreditPlanRequestLines`, `DevAxCustCreditGroups`, `DevAxCustCreditConcepts`, …).
Éstas cuatro son las que se pidieron; las demás se agregan cuando haya un consumidor que
las necesite.

## Los endpoints

`AuthorizationLevel.Function`: hace falta la function key en `x-functions-key`.

| Function | Ruta | Filtros |
|---|---|---|
| `Creditos_Clientes` | `GET /api/creditos/clientes` | `dataAreaId`, `cuenta` |
| `Creditos_Planes` | `GET /api/creditos/planes` | `dataAreaId`, `cuenta`, `creditId`, `requestId` |
| `Creditos_Cuotas` | `GET /api/creditos/cuotas` | `dataAreaId`, `cuenta`, `creditId` |
| `Creditos_Resoluciones` | `GET /api/creditos/resoluciones` | `dataAreaId`, `solicitudId` |

Todos aceptan además `top` (1 a 1000, default 100). Todos los filtros son opcionales: sin
ninguno se devuelve la tabla acotada por `top`.

**Las cuatro consultas van con `cross-company=true`**, así que devuelven los registros de
**todas las legal entities** del ambiente. Sin ese flag F&O responde sólo con la compañía
default del caller, y un satélite que consulta por `CustomerAccount` se llevaría un
subconjunto de los créditos del cliente sin ningún indicio de que le falta algo. Quien
quiera una sola compañía la pide con `dataAreaId` — **es el filtro el que acota, no la
falta del flag**. Por eso `dataAreaId` viene en todos los items de la respuesta: sin él, dos
filas de la misma cuenta en compañías distintas son indistinguibles.

```bash
# 302001 en INTE existe en cuatro legal entities: las cuatro vienen.
curl -s -H "x-functions-key: $KEY" "$APP/api/creditos/clientes?cuenta=302001"
```

```bash
curl -s -H "x-functions-key: $KEY" "$APP/api/creditos/cuotas?cuenta=302001&top=200"
```

**Sin CORS**, con el mismo criterio que [Customer Data](customerdata.md): el consumidor
llama server-to-server con la key, y un preflight anónimo sería superficie pública sin
nadie que la use.

### Qué devuelve

Las cuatro respuestas tienen la misma forma, para que el consumidor las parsee con el
mismo código:

```json
{
  "cantidad": 1,
  "truncado": false,
  "datos": [ { "dataAreaId": "us01", "CustomerAccount": "302001", "...": "..." } ]
}
```

**Los campos de `datos` viajan con el nombre que les pone F&O**, en PascalCase, sin
traducir. Es deliberado y es la diferencia con [Customer Data](customerdata.md): ahí hay
una transformación real (dos tablas de Dataverse se funden en una vista de negocio), acá
no hay ninguna decisión de mapeo que tomar. Inventar nombres nuevos sólo agregaría una
tabla de equivalencias que mantener cada vez que el ERP agregue una columna.

Lo único que se saca es `@odata.etag`, que no le sirve a nadie del otro lado.

### `truncado`, y por qué existe

`truncado: true` significa que F&O tenía más filas de las que entraban en `top`.

Se implementa pidiéndole a F&O **una fila más** que el tope y descartándola. Sin eso, el
consumidor no puede distinguir "son exactamente 100 cuotas" de "hay 340 y te di las
primeras 100" — que es justo el problema que quedó anotado como pendiente en
[Customer Data](customerdata.md#pendiente-de-documentar). Como el `$top` de la consulta ya
es `top + 1`, la fila extra viene en la primera página: nunca se pagina de más para
responder esta pregunta.

**No hay paginación real.** `top` topea en 1000 y no hay cursor. Para un cliente con más
de 1000 cuotas eso no alcanza, y la salida es filtrar por `creditId`. Si algún satélite
necesita recorrer la tabla entera, hay que agregar `skip` o exponer el `@odata.nextLink`.

### Enums y fechas: dos trampas del ERP

- **De los enums viaja la etiqueta, no el número** — `"Approved"`, `"Overdue"`, `"Yes"`.
  Igual criterio que en [Customer Data](customerdata.md): del otro lado hay un sistema
  externo, y el valor numérico sólo tiene sentido con la metadata de F&O al lado.

  | Campo | Valores |
  |---|---|
  | `GrantedPlanStatus` | `Invoiced`, `Overdue`, `Cancelled`, `Refinanced` |
  | `InstallmentStatus` | `Pending`, `Paid`, `Overdue`, `Refinanced` |
  | `Resolution` | `PendingInfo`, `Approved`, `Rejected` |
  | `RiskClassification` | `None`, `Low`, `Medium`, `High` |
  | `Homeowner`, `PoliticallyExposed`, `OperatesInGroup`, `PlanModified` | `Yes`, `No` |

  `ComplianceStatus` **no** es un enum: es texto libre del ERP.

- **F&O no manda fechas nulas, manda centinelas.** `1900-01-01` para "sin definir" y
  `2154-12-31T23:59:59Z` para "sin vencimiento". Se publican tal cual — convertirlas a
  `null` acá sería una decisión de negocio que este endpoint no tiene por qué tomar, pero
  **el consumidor tiene que tratarlas**, o va a mostrar cuotas pagadas en 1900.

- **La fecha de nacimiento viene desarmada** en `BirthDay`, `BirthMonth` (el enum
  `MonthsOfYear`, `"None"` si falta) y `BirthYear`, y los enteros valen `0` cuando no están
  cargados. No se arma una fecha con partes incompletas.

## Errores

| Situación | Respuesta |
|---|---|
| Un filtro que el endpoint no soporta | `400` con qué filtros acepta |
| `top` fuera de 1..1000 o no numérico | `400` |
| Sin resultados | `200` con `cantidad: 0` — no es un error |
| F&O caído, identidad sin permiso, filtro rechazado | `502` genérico; el detalle va al log |

**Un filtro no soportado es un `400`, no algo que se ignora.** El caso concreto es `cuenta`
en `/creditos/resoluciones`: si se ignorara, el satélite recibiría un `200` con la tabla
entera creyendo que filtró por cliente.

Los valores de los filtros se escapan con `FoOData.EscapeLiteral` antes de entrar al
`$filter` — es texto que llega de afuera y termina dentro de una expresión OData.

## Application Settings

Los del cliente OData del core, nada propio:

| Setting | Descripción |
|---|---|
| `FoBaseUrl` | URL del environment de F&O |
| `FoTenantId` | (DESA) Tenant del app registration |
| `FoClientId` | (DESA) Client Id; vacío ⇒ Managed Identity |
| `FoClientSecret` | (DESA) Secret del app registration |
| `KeyVaultUri` | Vault del que se leen los secretos |

> **La app no arranca sin `FoBaseUrl`** (`AddEipFoOData` tira al bindear las options).

Correrla local y probar con Postman: ver el
[readme del proyecto](../../../src/integrations/customers/AxxonCustomerCredit.Functions/readme.md).

## El techo de instancias

Esta app es la primera **API de lectura que sí pega a F&O**, y ninguno de los dos techos
que había servía:

| Techo | Valor | Para qué está |
|---|---|---|
| `foBoundMaxInstanceCount` | 1 | Los syncs (customers, products, customergroups). Protege los límites de API del ERP; nadie espera del otro lado |
| `maxInstanceCount` | 40 | Las apps que **no** tocan F&O (fiscal, customerdata) |
| `foReadApiMaxInstanceCount` | 5 | Ésta |

Con `1`, la latencia de cada consulta del satélite pasa a depender de cuántas haya en
vuelo. Con `40`, cuarenta instancias leyendo del ERP le compiten los límites de API a la
sincronización, que es la que no puede perder. El `5` es conservador y está para revisarse
con tráfico real: si el satélite empieza a comer `429`, **se sube ese número, no se cambia
de techo**. Ver [Infraestructura › Scale-out](../plataforma/infraestructura.md#scale-out-y-límites-de-fo).

## Cómo se estrena

El código, el Bicep (`deployCustomerCreditApp`) y
[el pipeline](../../../pipelines/azure-pipelines-customercredit.yml) ya están en el repo,
pero **los tres stages de deploy están apagados**. La app no existe en Azure todavía.
Prenderlos fuera de orden es lo que dejó a TicketAtención respondiendo 500 en INTE durante
días, así que el orden importa:

1. **Correr el pipeline de infra de INTE.** `deployCustomerCreditApp = true` ya está en
   `inte.bicepparam`, así que ese deploy crea `fa-axxoncustomercredit-inte` y su storage.
2. **Asignar a mano los roles de Storage y Key Vault**, mientras `deployRoleAssignments`
   siga en `false`. No es opcional: `AzureWebJobsStorage` va por identidad, así que sin los
   roles de Storage la app ni siquiera arranca. Comandos en
   [Ambientes](../plataforma/ambientes.md).
3. **Dar de alta la Managed Identity de la app en F&O INTE**, con lectura sobre las cuatro
   entidades `DevAxCustCredit*`. Sin eso los endpoints responden `502` sin más pistas.
4. **Poner `deployToInte: true`** en el pipeline y correrlo.
5. **Dar de alta la definición del pipeline en Azure DevOps** y autorizarle la service
   connection y el environment. El YAML en el repo no crea la definición — ver
   [Doble PR](../runbooks/doble-pr.md).

Para TEST son otra vez dos cambios, no uno: `deployCustomerCreditApp = true` en
`test.bicepparam` **y** `deployToTest: true` en el pipeline. Con el flag del pipeline en
true y la app sin crear, el stage muere en el `config-zip` con un `ResourceNotFound`.

Mientras tanto el pipeline no es inútil: el stage de Build compila el proyecto en cada
cambio.

## Pendiente de documentar

- **Quién es el satélite.** El endpoint está escrito contra un consumidor genérico
  server-to-server. Cuando se sepa cuál es, acá va su nombre, quién tiene la function key
  y cada cuánto consulta.
- **Cómo se cargan estas tablas en el ERP.** En INTE, `DevAxCustCreditCustomers` tiene
  filas (las de demo de `us01`/`us51`, no de Chacomer) y las otras tres están **vacías**:
  ni un plan, ni una cuota, ni una resolución. Los DTOs de planes, cuotas y resoluciones
  están armados contra el `$metadata`, no contra datos reales — la primera prueba con
  datos cargados puede sacar a la luz sentinelas o formatos que acá no están escritos.
- **Si hace falta paginar de verdad.** Hoy el techo es 1000 filas por llamada. Con un plan
  normal sobra; con el histórico de un cliente grande, no.
- **Tests.** No hay. `FoCreditoService` es testeable con un `IFoODataClient` falso — sobre
  todo el armado del `$filter` y el corte por `truncado`.
