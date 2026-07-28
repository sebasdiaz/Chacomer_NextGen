# AxxonCustomers.Functions

Azure Function (.NET 10 isolated) que sincroniza contacts de Dataverse hacia la entidad
**CustomersV3** de Finance & Operations cuando se califica un lead.

## Flujo

1. Un Service Endpoint de Dataverse publica el `RemoteExecutionContext` del mensaje
   **QualifyLead** en la cola **`leadcontacts`** del namespace de Service Bus
   **`dataverseinte`**.
2. `QualifyLeadCustomerSyncFunction` parsea el mensaje y extrae el Id del contact de
   `InputParameters -> OpportunityCustomerId` (solo cuando `LogicalName == "contact"`;
   si el customer es un account, el mensaje se completa sin procesar).
3. `ContactCustomerSyncService` recupera el contact (`contactid == Id`) con las columnas
   que pide el mapeo y `FoPayloadBuilder` arma el payload resolviendo los lookups
   relacionados (party number, grupo de clientes, moneda, terminos de pago, etc.).
4. `FoCustomerService` hace `POST {FoBaseUrl}/data/CustomersV3` con el payload mapeado.
   La compania destino viaja en `dataAreaId` (tomado de `msdyn_company.cdm_companycode`).
5. El `CustomerAccount` generado por F&O (number sequence) se escribe de vuelta en
   `msdyn_contactpersonid` del contact (write-back). Esto ademas da **idempotencia**:
   si el contact ya tiene `msdyn_contactpersonid`, el insert se omite.

## Mapeo (por JSON, no hardcodeado)

El mapeo CRM -> AX no vive en codigo: sale de `Mappings/`, dos archivos por entidad.

| Archivo | Que es | Se edita |
|---|---|---|
| `customersv3.{entidad}.dualwrite.json` | Export del Table Map de Dual Write, tal cual lo baja el funcional | **Nunca**. Se reemplaza entero al re-exportar |
| `customersv3.{entidad}.overlay.json` | Lo que el esquema de Dual Write no puede expresar | Si |

El export esta escrito en direccion **AX -> CRM** (`sourceField` = campo de F&O,
`destinationField` = campo de Dataverse). El compilador lo lee invertido. Dejarlo intacto
es lo que permite que un re-export sea un diff limpio en el PR.

### Que aporta el overlay

| Seccion | Para que |
|---|---|
| `target` | Entity set de F&O y logical name de Dataverse |
| `company` | `dataAreaId` — Dual Write lo resuelve por particion, no por field mapping |
| `key` | Campo de write-back y campos con los que se busca el registro existente |
| `syncWhen` | Guarda: condiciones que el registro debe cumplir para sincronizarse |
| `ignore` | Filas del export que no aplican en nuestra direccion |
| `constants` | Valores fijos (ej. `PartyType`), ganan sobre el export |
| `fields` | Corrige o agrega mapeos |

Precedencia: export -> `ignore` -> `fields` -> `constants`.

### Los cinco `kind`

| `kind` | Que hace |
|---|---|
| `direct` | Valor del atributo tal cual (string, int, Money, bool, fecha) |
| `lookup` | Path `atributo.atributoRelacionado`: recupera el campo de la entidad relacionada |
| `valueMap` | Renderiza el valor a string canonico y lo busca en `map` (cubre OptionSet -> enum y bool -> Yes/No) |
| `label` | Texto si el atributo es string; etiqueta formateada si es OptionSet |
| `const` | Valor fijo |

Cerrados a proposito: lo que no entre aca se resuelve en C# con nombre propio, no en un
JSON sin debugger.

### Dos cosas que el export no puede resolver solo

**Case de los value maps.** En direccion AX -> CRM el destino es un int de OptionSet y el
case da igual, por eso el export trae `"all"`, `"yes"`, `"organization"`. En nuestra
direccion el destino es un literal de enum de la API OData, que es case-sensitive. Los
overrides de `fields` en el overlay de account son solo correccion de case.

**Case de los nombres de campo.** El export escribe los campos de F&O en MAYUSCULAS
(`PARTYNUMBER`), que son los nombres de la tabla de AX; la API espera `PartyNumber`, y no
siempre es un PascalCase derivable (`A365Sellable`, no `A365SELLABLE`). `FoSchemaProvider`
lo resuelve sondeando un registro del entity set — una vez por proceso, cacheado.

### Validacion

Los mapeos se compilan **al arranque** y la app no levanta si alguno falla: un mapeo mal
escrito no tira excepcion, escribe mal en F&O y nadie se entera por semanas. Se acumulan
todos los errores y se reportan juntos. Se valida que:

- ningun `valueMap` sea ambiguo al invertirse (dos valores de AX cayendo en el mismo de CRM),
- el campo de `key.writeBack` este mapeado,
- todos los `key.matchOn` tengan un campo que los alimente,
- ningun campo de F&O quede mapeado dos veces,
- los `kind` existan y traigan lo que necesitan (`map`, `related`, `value`).

Lo que **no** se valida al arranque es contra metadata: que el atributo exista en
Dataverse y que el campo exista en F&O. Eso se resuelve en el primer mensaje (ver
`FoSchemaProvider`) para no atar el cold start a la disponibilidad de F&O.

### Tests

`tests/AxxonCustomers.Functions.Tests` — el compilador de mapeos, el armado del payload y
una guarda de drift sobre los archivos que se deployan.

```bash
dotnet test tests/AxxonCustomers.Functions.Tests/AxxonCustomers.Functions.Tests.csproj
```

Corren en el pipeline antes del publish: si caen, no se genera artifact.

`ShippedMappingsTests` compila los JSON reales y afirma las decisiones que tomamos
(`PartyType` constante, `A365Sellable = Yes`, contact sin campos de credito, account solo
organizaciones, literales de enum capitalizados). **Que fallen despues de un re-export no
significa que el export este mal — significa que hay que mirarlo y decidir de nuevo.**

## Manejo de errores (autoComplete = false)

| Situacion                                            | Accion                                  |
|------------------------------------------------------|-----------------------------------------|
| Body no parseable como RemoteExecutionContext        | DLQ (`ParseFailed`)                     |
| QualifyLead sin contact (customer = account o nulo)  | Complete sin procesar                   |
| El registro no cumple `syncWhen`                     | Complete sin procesar                   |
| Contact inexistente / sin `msdyn_company`            | DLQ (`DataError`)                       |
| Campo del mapeo inexistente en F&O                   | DLQ (`DataError`)                       |
| Contact ya sincronizado (`msdyn_contactpersonid`)    | Complete sin re-insertar (idempotencia) |
| F&O rechaza por regla de negocio (400, 404, 409, 422) | DLQ (`BusinessRuleFailed`) **sin reintentar**, con el Infolog de F&O como descripcion |
| Error transitorio (F&O 5xx/429/timeout, Dataverse, red) | Abandon -> retry de Service Bus -> DLQ tras Max Delivery Count |

> Un 400 de F&O es permanente: significa que el dato viola una regla (un customer group
> que no existe en la compania, un party que ya existe como prospect). Reintentarlo solo
> consume delivery count y martilla F&O. La clasificacion vive en `FoODataException.IsPermanent`.
> Los 401/403 **si** se reintentan: suelen ser un token vencido o un permiso que no propago.

## Application Settings

| Setting                 | Descripcion                                                       |
|-------------------------|-------------------------------------------------------------------|
| `ServiceBusConnection`  | Connection string (o config de identity) del namespace `dataverseinte` |
| `ServiceBusQueueName`   | `leadcontacts`                                                    |
| `DataverseUrl`          | URL del environment de Dataverse                                  |
| `DataverseClientId`     | (DESA) Client Id del app registration; vacio => Managed Identity  |
| `DataverseClientSecret` | (DESA) Secret del app registration                                |
| `FoBaseUrl`             | URL base del environment de F&O                                   |
| `FoTenantId`            | (DESA) Tenant para client-credentials contra F&O                  |
| `FoClientId`            | (DESA) Client Id; vacio => Managed Identity                       |
| `FoClientSecret`        | (DESA) Secret                                                     |

> En produccion usar Managed Identity de la Function App tanto para Dataverse
> (application user) como para F&O (registrar el client id en
> *System administration > Microsoft Entra applications*).
