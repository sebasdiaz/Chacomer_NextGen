<!-- wiki-meta
sources:
  - src/integrations/customers/AxxonCustomerData.Functions/**
  - pipelines/azure-pipelines-customerdata.yml
last_reviewed: 2026-09-04
-->

# Customer Data — consulta de clientes por RUC

Azure Function (.NET 10 isolated) que expone **un endpoint HTTP de solo lectura** sobre
Dataverse para que un satélite externo traiga los datos de un cliente por RUC.

No consume Service Bus, no escribe nada y no habla con F&O. Es la contracara de
[Customers](customers.md): ahí los datos *salen* hacia el ERP por cola; acá se *leen* por
HTTP, a pedido de quien consulta.

## Por qué es una app aparte

Las dos alternativas se descartaron por lo mismo — cada una le colgaba la superficie
pública a un lugar que no la aguanta:

- **`fa-axxoncustomers`** corre con `foBoundMaxInstanceCount = 1` porque el techo de
  instancias es lo que protege los límites de API de F&O (ver
  [Infraestructura › Scale-out](../plataforma/infraestructura.md#scale-out-y-límites-de-fo)).
  Meter ahí una API pública ata el tiempo de respuesta del satélite a la cola de
  sincronización, y al revés: un satélite en un loop compite con el alta de clientes.
- **`fa-axxonfiscal`** ya es *la* superficie de consulta por RUC, pero su dominio son las
  consultas fiscales (SET/DNIT, TURUC). `Dataverse_ConsultaRuc` ya le rompió esa pureza una
  vez; sumarle la ficha del cliente la convertía en el cajón de sastre de todo lo que se
  consulta por RUC.

El costo de la decisión, para tenerlo escrito: **es una Function App más** —su storage, su
Managed Identity, su alta como Application User y su autorización de service connection en
ADO. Ese alta manual es exactamente lo que dejó a TicketAtencion respondiendo 500 en INTE
durante días.

## El endpoint

`AuthorizationLevel.Function`: hace falta la function key en `x-functions-key`.

| Function | Ruta |
|---|---|
| `Clientes_ConsultaPorRuc` | `GET /api/clientes?ruc=XX` |

```bash
curl -s -H "x-functions-key: $KEY" "$APP/api/clientes?ruc=80054203-7"
```

**Sin CORS**, a diferencia de [Fiscal](fiscal.md) y
[Ticket de Atención](ticketatencion.md). El consumidor llama server-to-server con la key;
un preflight anónimo sería superficie pública sin nadie que la use. Si algún día lo consume
un web resource, se agrega el `OPTIONS` y el origen se cablea desde el Bicep
(`allowedOrigins`), como en TicketAtención — nunca un `*` a mano.

### Qué devuelve

Un RUC devuelve normalmente **varias filas**: el master más los raws que cuelgan de él, uno
por legal entity — ver [Contacts](contacts.md). Por eso la respuesta es una lista y no un
registro único. Vienen los accounts primero y, dentro de cada tabla, el master antes que los
raws. Tope de 50 por tabla.

```json
{
  "ruc": "80054203-7",
  "cantidad": 2,
  "clientes": [
    {
      "id": "...",
      "entidad": "account",
      "tipoPersona": "Juridica",
      "nombre": "CHACOMER S.A.",
      "identificationNumber": "80054203-7",
      "esMaster": true,
      "masterId": null,
      "customerAccount": null,
      "legalEntity": null,
      "tipoPersoneriaJuridica": "Sociedad Anonima",
      "tipoDocumento": "RUC",
      "email": "...",
      "telefono": "...",
      "activo": true
    }
  ]
}
```

| Campo | De dónde sale |
|---|---|
| `entidad`, `tipoPersona` | La tabla: `account` → `"Juridica"`, `contact` → `"Fisica"`. No hay campo con esa semántica |
| `nombre` | `fullname` (contact) / `name` (account) |
| `identificationNumber` | `msdyn_identificationnumber`, tal cual está guardado |
| `esMaster`, `masterId` | `axx_ismaster` y `axx_mastercontactid` / `axx_masteraccountid` |
| `customerAccount` | El write-back de [Customers](customers.md): `msdyn_contactpersonid` / `accountnumber` |
| `legalEntity` | Lookup `msdyn_company`: id y nombre de la EntityReference, `codigo` de `cdm_companycode` |
| `tipoPersoneriaJuridica` | Etiqueta de `axx_tipopersoneriajuridica` (Lookup a `axx_personeriajuridia`) |
| `tipoDocumento` | Etiqueta del OptionSet: `axx_tipodocumento` en contact, **`axx_tipodedocumento`** en account |
| `email`, `telefono` | `emailaddress1`, `telephone1` |
| `activo` | `statecode = 0` |

Tres cosas que confunden si no están escritas:

- **`customerAccount` vacío no significa que el cliente no exista en F&O.** Los masters no
  se sincronizan al ERP, y un raw recién creado puede estar todavía en la cola.
- **`legalEntity` puede venir null**, y por eso el link a `cdm_company` es `LeftOuter`: con
  un inner join esas filas desaparecerían de la respuesta en vez de venir sin compañía.
  Conceptualmente el master es el que no debería tenerla —es la vista unificada del RUC,
  no la fila de una legal entity—, pero **en INTE hay masters con `msdyn_company` y hasta
  con `customerAccount` poblados** (verificado el 2026-09-01 con el RUC `345678`). No
  asumir que master implica compañía vacía: el consumidor tiene que mirar `esMaster`.
- **De los OptionSet viaja la etiqueta, no el número.** Del otro lado hay un sistema
  externo: el valor numérico solo tiene sentido con la metadata de Dataverse al lado.
- **El tipo de documento se llama distinto en cada tabla**: `axx_tipodocumento` en contact
  y `axx_tipodedocumento` —con el "de" en el medio— en account. Verificado contra la
  metadata de INTE y de TEST: los dos ambientes lo tienen así, no es el drift de uno. Y en
  las dos tablas es un **OptionSet**, no un lookup. Pedirle a account el nombre de contact
  hace que el `RetrieveMultiple` tire, y como accounts se consulta primero, se cae la
  respuesta entera con un 502.

### El RUC, con o sin dígito verificador

`80054203-7` matchea por igualdad y `80054203` por prefijo `80054203-`. El guion del prefijo
es lo que evita que `8005420` arrastre registros ajenos. **Es el mismo filtro que
`Dataverse_ConsultaRuc` de [Fiscal](fiscal.md)**, a propósito: los dos buscan sobre el mismo
campo y tienen que devolver el mismo conjunto de registros. Si uno cambia, cambian los dos.

### Una consulta por tabla, y nada más

Todo lo que devuelve sale de dos `RetrieveMultiple` (una por tabla), con el código de la
legal entity resuelto por `LinkEntity`. **No hay una segunda vuelta por registro**, y ese
techo es deliberado: un endpoint que dispara N consultas por respuesta se vuelve el cuello
de botella de Dataverse el día que un satélite lo llame en un loop.

Lo que queda afuera por esa misma razón: **dirección, país y región** viven en
`customeraddress` y pedirlas es una consulta por cliente. Cuando hagan falta, la decisión a
tomar es si se paga el N+1 o si se resuelven con un `$expand` por Web API — no agregarlas de
a una sin mirar el costo.

## Errores

| Situación | Respuesta |
|---|---|
| Falta el parámetro `ruc` | `400` con `{"error":"El parametro 'ruc' es requerido."}` |
| RUC sin resultados | `200` con `cantidad: 0` — no es un error |
| Dataverse caído, MI sin Application User, query inválida | `502` genérico; el detalle va al log |

El `502` es genérico a propósito: del otro lado hay un sistema externo, no el equipo que
puede leer App Insights.

## Application Settings

| Setting | Descripción |
|---|---|
| `DataverseUrl` | URL del environment de Dataverse |
| `DataverseClientId` | (DESA) Client Id del app registration; vacío ⇒ Managed Identity |
| `DataverseClientSecret` | (DESA) Secret del app registration |
| `KeyVaultUri` | Vault del que se leen los secretos |
| `DataverseClientSecretName` | Nombre del secret en el vault cuando no coincide con la clave |

> **La app no arranca sin `DataverseUrl`** (`AddEipDataverse` tira al bindear las options), y
> eso es a propósito: es lo único que no tiene default razonable.

## Estado y despliegue

| Ambiente | App | Estado |
|---|---|---|
| INTE | `fa-axxoncustomerdata-inte` | La crea el Bicep (`deployCustomerDataApp = true`) |
| TEST | — | `deployCustomerDataApp = false`. Todavía no se promovió |

**Promover a TEST son dos cambios, no uno:** `deployCustomerDataApp = true` en
`test.bicepparam` y `deployToTest: true` en `pipelines/azure-pipelines-customerdata.yml`. Con
el flag del pipeline en true y la app sin crear, el stage muere en el `config-zip` con un
`ResourceNotFound`.

El `false` de TEST es explícito y no un olvido: ahí `deployFunctionApps` está en `true`, así
que sin el param el próximo deploy de infra crearía la app y su storage de rebote.

### Lo que el deploy no hace, y sin lo cual el endpoint responde 502

1. **Dar de alta la Managed Identity de la app como Application User en Dataverse**, con
   lectura sobre `contact`, `account` y `cdm_company`. Sin eso la primera consulta falla y el
   caller ve un `502` sin más pistas. Ver
   [Secretos y Key Vault](../plataforma/secretos-y-key-vault.md).
2. **Asignar a mano los roles de Storage y Key Vault** mientras `deployRoleAssignments` esté
   en `false`. No es opcional: `AzureWebJobsStorage` va por identidad, así que sin los roles
   de Storage la app ni siquiera arranca. Comandos en
   [Ambientes](../plataforma/ambientes.md).
3. **Dar de alta la definición del pipeline en Azure DevOps** y autorizarle la service
   connection y el environment. El YAML en el repo no crea la definición — ver
   [Doble PR](../runbooks/doble-pr.md).

## Pendiente de documentar

- **Quién es el satélite.** El endpoint está escrito contra un consumidor genérico
  server-to-server; cuando se sepa cuál es, acá va su nombre, quién tiene la function key y
  cada cuánto consulta.
- **Si hace falta paginar.** Hoy el tope es 50 por tabla y se corta sin avisar. Con un RUC
  normal sobran; con un RUC sucio, el consumidor no se entera de que hay más.
