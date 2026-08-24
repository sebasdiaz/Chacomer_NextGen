<!-- wiki-meta
sources:
  - src/integrations/customers/AxxonCustomers.Functions/**
  - tests/AxxonCustomers.Functions.Tests/**
  - pipelines/azure-pipelines-customers.yml
last_reviewed: 2026-08-21
-->

# Customers — Dataverse → F&O (CustomersV3)

Azure Functions (.NET 10 isolated) que sincronizan contacts y accounts de Dataverse hacia
la entidad **CustomersV3** de Finance & Operations.

## Las dos funciones, y por que son dos

El reparto lo decide **una sola cosa**: `cdm_isenabledfordualwrite` de la `cdm_company`
del registro.

| Legal entity | Alta | Modificacion |
|---|---|---|
| En Dual Write (`= true`) | `QualifyLeadCustomerSyncFunction` | Dual Write |
| Fuera de Dual Write (`= false`) | `CustomerFoSyncFunction` | `CustomerFoSyncFunction` |
| Indeterminada (flag sin setear) | `QualifyLeadCustomerSyncFunction` | — |

Dual Write en direccion CRM -> AX **solo actualiza customers existentes, nunca crea** (su
filtro pide `msdyn_contactpersonid ne null`). Por eso hace falta el flujo de QualifyLead
incluso en las companias que si sincroniza. Y por eso las legal entities que Dual Write no
cubre necesitan alguien que haga las dos cosas: eso es `CustomerFoSyncFunction`.

El reparto es **excluyente a proposito**. Si los dos flujos procesaran el mismo contact,
el alta saldria por duplicado y F&O rechazaria la segunda con un 400.

> **La polaridad del flag importa.** Lo que mandamos por API es la company con el flag en
> `false`. En Dataverse un Yes/No que nunca se seteo no viene en el Retrieve, y
> `GetAttributeValue<bool>` lo leeria como `false` — o sea "mandalo". Por eso el flag
> ausente es `Unknown` y **no se sincroniza**: con el campo despoblado, lo contrario
> mandaria el maestro de clientes entero a F&O. Vive en
> `Axxon.Eip.Core/Dataverse/DualWriteCompanyResolver.cs`.

### QualifyLead (legal entities en Dual Write)

1. Un Service Endpoint de Dataverse publica el `RemoteExecutionContext` del mensaje
   **QualifyLead** en la cola **`leadcontacts`**.
2. `QualifyLeadCustomerSyncFunction` parsea el mensaje y extrae el Id del contact de
   `InputParameters -> OpportunityCustomerId` (solo cuando `LogicalName == "contact"`;
   si el customer es un account, el mensaje se completa sin procesar).
3. Si la legal entity del contact **no** esta en Dual Write, se completa sin procesar:
   ese contact lo toma `CustomerFoSyncFunction`.
4. `CustomerSyncService` hace el resto (ver abajo).

### fo-sync (legal entities fuera de Dual Write)

1. `AxxonContacts.Functions`, despues del master matching, resuelve la legal entity del
   raw y —si esta fuera de Dual Write— publica un envelope EiP en la cola
   **`customer-fo-sync`** (sessions por id de registro, para que dos modificaciones del
   mismo cliente no se procesen fuera de orden).
2. `CustomerFoSyncFunction` lee el envelope. El `entityType` es el nombre del mapeo
   (`account` o `contact`) y el payload trae solo el `recordId`: **es una referencia, no
   un snapshot**. El consumidor relee Dataverse porque el snapshot de un evento Update
   llega parcial (es un delta) y mapear desde ahi escribe mal en el ERP.

### Lo que hacen las dos (`CustomerSyncService`)

1. Recupera el registro con las columnas que pide el mapeo; `FoPayloadBuilder` arma el
   payload resolviendo los lookups (party number, grupo de clientes, moneda, terminos de
   pago, etc.).
2. Busca el customer en F&O por `PartyNumber` / `CustomerAccount` dentro de la compania.
   **La existencia se verifica contra F&O, no contra el campo de CRM**: el write-back
   puede tener valor sin que el customer exista.
3. Si no existe: `POST {FoBaseUrl}/data/CustomersV3`. El `CustomerAccount` que genera F&O
   por number sequence vuelve al campo de write-back de CRM (`msdyn_contactpersonid` en
   contact, `accountnumber` en account).
4. Si existe: `PATCH {FoBaseUrl}/data/CustomersV3(dataAreaId='cha',CustomerAccount='...')`.

**En la modificacion, vaciar un campo en CRM no lo vacia en F&O.** Los nulls se omiten
igual que en el alta: el mapeo no sabe distinguir "el usuario borro el dato" de "el campo
nunca se completo", y mandar null por las dudas pisa datos que pueden venir de otra fuente
dentro del ERP.

> Para que las modificaciones lleguen, los **filtering attributes** del step del Service
> Endpoint en Dataverse tienen que cubrir las columnas del mapeo (`map.Columns`). Esa
> config vive en Dataverse, no en el repo. Si falta un campo, el update no dispara y no
> hay error en ningun lado — es la falla mas silenciosa de todo el flujo.

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
| `key` | Campo de write-back, campos con los que se busca el registro existente, y los que no viajan en el PATCH |
| `syncWhen` | Guarda: condiciones que el registro debe cumplir para sincronizarse. Hoy: contact `msdyn_sellable eq true`, account `customertypecode eq 3` |
| `ignore` | Filas del export que no aplican en nuestra direccion |
| `constants` | Valores fijos (ej. `PartyType`), ganan sobre el export |
| `fields` | Corrige o agrega mapeos |

Precedencia: export -> `ignore` -> `fields` -> `constants`.

> **Quien pone el `msdyn_sellable = true` que pide la guarda del contact:** el flujo de
> QualifyLead, justo antes de sincronizar, con el valor del App Setting
> `QualifyLeadSellableValue` (`SellableStamper`). fo-sync **no** sella: lee lo que haya.
> Si se saca el setting, nadie escribe el campo y solo sincronizan los contacts que ya
> venian sellables — que era la conducta anterior, y el motivo por el que un prospecto
> recien calificado podia saltearse en silencio.

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
- todos los `key.immutable` apunten a un campo que exista en el mapeo (excluir de la
  actualizacion algo que nunca se manda es una declaracion muerta, casi siempre un typo),
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
(`PartyType` constante e inmutable, `A365Sellable = Yes`, contact solo sellable y sin
campos de credito, account solo organizaciones, literales de enum capitalizados).
**Que fallen despues de un re-export no significa que el export este mal — significa que
hay que mirarlo y decidir de nuevo.**

## Manejo de errores (autoComplete = false)

| Situacion                                            | Accion                                  |
|------------------------------------------------------|-----------------------------------------|
| Body no parseable (RemoteExecutionContext / envelope EiP) | DLQ (`ParseFailed` / `DeserializationFailed`) |
| Envelope sin `entityType` o sin `recordId`           | DLQ (`ContractViolation`)               |
| QualifyLead sin contact (customer = account o nulo)  | Complete sin procesar                   |
| QualifyLead sobre una legal entity fuera de Dual Write | Complete sin procesar (lo toma fo-sync) |
| El registro no cumple `syncWhen`                     | Complete sin procesar                   |
| Registro inexistente / sin `msdyn_company`           | DLQ (`DataError` / `ContractViolation`) |
| Campo del mapeo inexistente en F&O                   | DLQ (`DataError` / `ContractViolation`) |
| El customer ya existe en F&O                         | PATCH (antes: se omitia el insert)      |
| F&O rechaza por regla de negocio (400, 404, 409, 422) | DLQ (`BusinessRuleFailed`) **sin reintentar**, con el Infolog de F&O como descripcion |
| Error transitorio (F&O 5xx/429/timeout, Dataverse, red) | Abandon -> retry de Service Bus -> DLQ tras Max Delivery Count |

> Un 400 de F&O es permanente: significa que el dato viola una regla (un customer group
> que no existe en la compania, un party que ya existe como prospect). Reintentarlo solo
> consume delivery count y martilla F&O. La clasificacion vive en `FoODataException.IsPermanent`.
> Los 401/403 **si** se reintentan: suelen ser un token vencido o un permiso que no propago.

## Application Settings

| Setting                 | Descripcion                                                       |
|-------------------------|-------------------------------------------------------------------|
| `ServiceBusConnection`  | Connection string (o config de identity) del namespace de Service Bus |
| `ServiceBusQueueName`   | `leadcontacts`                                                    |
| `FoSyncServiceBusQueueName` | `customer-fo-sync`                                            |
| `QualifyLeadSellableValue` | Valor que QualifyLead escribe en `msdyn_sellable` del contact antes de sincronizar (`true`). Ausente o no booleano = no se sella nada |
| `DataverseUrl`          | URL del environment de Dataverse                                  |
| `DataverseClientId`     | (DESA) Client Id del app registration; vacio => Managed Identity  |
| `DataverseClientSecret` | (DESA) Secret del app registration                                |
| `FoBaseUrl`             | URL base del environment de F&O                                   |
| `FoTenantId`            | (DESA) Tenant para client-credentials contra F&O                  |
| `FoClientId`            | (DESA) Client Id; vacio => Managed Identity                       |
| `FoClientSecret`        | (DESA) Secret                                                     |
| `KeyVaultUri`           | Vault del que se leen los secretos. En INTE: `https://keyvaultinte.vault.azure.net/` |
| `DataverseClientSecretName` / `FoClientSecretName` | Nombre del secret en el vault cuando no coincide con la clave. En INTE ambos: `SecretNextGenDynamics365Inte` |

> En produccion usar Managed Identity de la Function App tanto para Dataverse
> (application user) como para F&O (registrar el client id en
> *System administration > Microsoft Entra applications*).

> **Las dos colas comparten `ServiceBusConnection`**, asi que `leadcontacts` y
> `customer-fo-sync` tienen que vivir en el **mismo namespace**. Las dos las crea
> `infra/modules/servicebus.bicep` en el namespace de la EiP (`sb-chacomer-eip-{env}`):
> `customer-fo-sync` con sessions, `leadcontacts` sin sessions (el Service Endpoint de
> Dataverse no setea `SessionId`).
>
> **INTE es la excepcion:** ahi `leadcontacts` quedo en el namespace viejo
> (`dataverseinte`) y la app la consume con connection string SAS. Se unifica en el
> cutover a Managed Identity; el template no toca esa cola.

> **El deploy de infra no alcanza para que llegue el primer mensaje.** Crear la cola no
> conecta nada: hay que apuntar el **Service Endpoint de Dataverse** del ambiente a
> `sb-chacomer-eip-{env}` / `leadcontacts`, con una **SAS policy Send** sobre la cola (el
> plugin corre en el sandbox de Dataverse, no tiene Managed Identity). Sin eso la cola
> queda vacia y el flujo de QualifyLead no falla en ningun lado: simplemente no pasa nada.
