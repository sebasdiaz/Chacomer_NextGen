<!-- wiki-meta
sources:
  - src/integrations/customers/AxxonCustomers.Functions/Mapping/Ltm*
  - src/integrations/customers/AxxonCustomers.Functions/Services/LtmCust*
last_reviewed: 2026-08-25
-->

# ADR-001 — LTMCustTable se sincroniza por cola propia, con payload de referencia

**Estado:** Aceptada
**Fecha:** 2026-08-25

## Contexto

`LTMCustTable` es la tabla de localización Paraguay que acompaña a `CustTable` en F&O.
Es **1:1 con el cliente**, con clave `(dataAreaId, AccountNum)`, y ya tiene data entity
publicada en OData. Cuando se da de alta un `contact` o un `account` en Dataverse y el
cliente se crea en F&O, hay que completar también esta fila.

Tres hechos condicionan el diseño:

1. **`AccountNum` es el `CustomerAccount` que genera F&O por number sequence.** No existe
   antes del alta: llega al registro de Dataverse por el write-back que hace
   `CustomerSyncService`. Cualquier disparador que corra antes de eso no tiene la clave de
   la fila.
2. **Cuatro de sus campos salen de virtual entities de F&O** —
   `mserp_ltmaccounttypegroupentity`, `mserp_ltmtaxpayertypeentity`,
   `mserp_ltmtaxpayerdoctypeentity`, `mserp_ltmaddresscountryregionentity`. Cada Retrieve
   sobre una virtual entity es Dataverse llamando en vivo a la OData de F&O.
3. **`LTMCustTable` es la única tabla de la localización que hay que escribir.** El resto
   es de sólo lectura y se resuelve con virtual entities, sin sincronizar nada.

## Decisión

Una cola propia — **`customer-ltm-sync`**, con sessions por `recordId` — y una Function
que relee Dataverse y escribe la fila de `LTMCustTable`.

- El payload es una **referencia** (`CustomerSyncPayload`: `recordId` y `dataAreaId`), no un
  snapshot, igual que `customer-fo-sync` (decisión #2). El `CustomerAccount` **no** viaja en
  el mensaje: el consumidor lo relee del registro, que es la fuente de verdad.
- El mapeo **se escribe en C#** (`LtmCustPayloadBuilder`), con tests, no en un overlay JSON.
  Es la excepción que el propio motor tiene prevista: sus cinco primitivas están cerradas a
  propósito y este mapeo no entra en ellas (ver *El mapeo no entra en el motor JSON*, abajo).
  Sí reusa `FoSchemaProvider` para el casing de las propiedades OData e `IFoODataClient`
  para escribir.
- **Un solo disparador:** `CustomerSyncService`, después del write-back del
  `CustomerAccount`. Cubre el alta por los dos flujos (QualifyLead y fo-sync).
- **Guarda:** sin `CustomerAccount` en el cliente no se sincroniza — se completa el mensaje
  sin procesar, no va a DLQ.
- **La escritura es un POST y nada más.** No se consulta si la fila existe ni se actualiza:
  se manda el JSON armado y, si F&O lo rechaza, el mensaje va al DLQ como
  `BusinessRuleFailed` y no se procesa. Esto se apoya en un hecho verificado del ERP:
  **F&O no crea la fila de `LTMCustTable` al insertar el `CustTable`**. Si eso cambiara —una
  actualización de la localización, por ejemplo— todas las altas empezarían a dar 400 por
  clave duplicada y habría que pasar a PATCH.

El **backfill** de los clientes que ya existían va por un HTTP trigger con function key
(`LtmCustBackfillFunction`), que enumera y **encola** en la misma cola. No es un timer a
propósito: sin verificación previa el backfill no es idempotente, y un timer "que después
deshabilitamos" repite el maestro de clientes entero contra el DLQ el día que nadie lo
apague. Va con `dryRun` por default; encolar de verdad hay que pedirlo.

### Alcance de la v1: solo el alta

La modificación queda **fuera de alcance a propósito**. Sin PATCH, encolar también los
cambios produciría un POST sobre una fila que ya existe, un 400 y un mensaje en el DLQ por
cada modificación de cliente — y el DLQ dejaría de servir como señal de que algo anda mal.

Cuando se sume la modificación hará falta un **segundo disparador desde AxxonContacts** que,
a diferencia de `FoSyncDispatcher`, **no filtre por Dual Write**: en las legal entities que
sí están en Dual Write las modificaciones las hace Dual Write y nunca pasan por nuestras
Functions, así que ese hueco no lo cubre nadie hoy. Junto con eso hay que decidir si la
escritura pasa a ser un upsert (`FindFirst` → `POST`/`PATCH`) o si alcanza con PATCH.

## El mapeo no entra en el motor JSON

El mapeo funcional de `contact` trae cuatro cosas que las cinco primitivas de
`FieldKind` no cubren:

| Campo | Cadena | Qué falta |
|---|---|---|
| `AccountTypeGroupId` | `mserp_ltmaccounttypegroupentity` donde company = X y `CustVendEntity = "Customer"` | No es navegación: es una consulta con filtro |
| `CountryRegionId`, `StateId` | `customeraddress.axx_pais` / `.axx_region` | `customeraddress` es **1:N**: hay que elegir cuál dirección |
| `CountryDocTypeId`, `TaxPayerTypeId` | `contact.axx_tipodocumento` → dos campos de la misma fila | El motor indexa los mapeos **por atributo de CRM**: uno no puede alimentar dos campos |
| `CountryDocNum`, `StateDocNum` | `msdyn_identificationnumber` → dos campos | Ídem |

> **Dos de las cuatro tablas de localización no aparecen en el código, y está bien.** El
> análisis funcional nombra `mserp_ltmaccounttypegroupentity`,
> `mserp_ltmtaxpayerdoctypeentity`, `mserp_ltmtaxpayertypeentity` y
> `mserp_ltmaddresscountryregionentity`, pero el mapeo solo navega las dos primeras. Las
> otras dos son el **catálogo al que apuntan las tablas custom de Dataverse**
> (`axx_tipodocumento`, `axx_pais`), así que se llega al mismo código por otro camino:
> `TaxPayerTypeId` sale de la fila de tipos de documento, y `CountryRegionId` de
> `axx_pais.axx_countryregion`. Confirmado con el cliente — no es una omisión.

`EntityMap.cs` declara la política para exactamente este caso: *cinco primitivas, cerradas
a propósito: lo que no entre acá se resuelve en C# con nombre propio y tests, no en un JSON
sin debugger*. Agregar consultas con filtro, selección sobre relaciones 1:N y mapeos de
uno-a-muchos campos convertiría los overlays en un mini-lenguaje de queries al servicio de
un solo consumidor. Por eso este mapeo va en C#, y los overlays de `CustomersV3` quedan
como están.

> **El `dataAreaId` no puede salir de `systemuser.cdm_company`.** El mapeo funcional lo
> resuelve así porque fue pensado desde CRM, donde el usuario que carga el cliente pertenece
> a su company — que es como Dual Write resuelve la partición. En una Function el usuario que
> ejecuta es el **application user de la Managed Identity**: su company es una sola y fija.
> Todos los clientes caerían en la misma legal entity, y encima en una distinta de la que usó
> `CustomersV3` (que lo saca de `contact.msdyn_company`), con lo cual el `AccountNum` ni
> existiría ahí. Se usa `contact.msdyn_company`, igual que el overlay de `CustomersV3`.
> Lo mismo vale para `AccountTypeGroupId`, que arranca de la misma cadena.

## Alternativas descartadas

**Extender `CustomersV3` en F&O** para que incluya los campos de la localización. Es la
única opción atómica y no requiere código nuevo del lado EiP. Se descarta porque exige
trabajo en el modelo de F&O y porque tocar la entidad estándar arriesga el mapa de Dual
Write de las companies que sí lo usan. **Si algún día hay que hacer dev en F&O por otro
motivo, vale reconsiderarla.**

**Escribir `LTMCustTable` como paso satélite dentro del sync de customers**, en el mismo
mensaje. Es la de menor superficie — cero infra nueva. Se descarta por el hueco de Dual
Write descrito arriba: la fila sólo se escribiría en el alta para las companies que Dual
Write cubre.

**Un plugin que arma el JSON de `LTMCustTable` en un campo de `contact`/`account` y lo
publica a una cola.** Se descarta por tres motivos: (a) el plugin no tiene el `AccountNum`
en el Create, y hacerlo disparar en el Update del write-back lo acopla al sync de customers
por un canal invisible; (b) el mapeo saldría de los JSON versionados —perdiendo la
validación de arranque, los tests de drift y el `FoSchemaProvider` que resuelve el casing
real de las propiedades OData— y pasaría a deployarse registrando un assembly con PRT;
(c) el campo se convierte en una segunda fuente de verdad que queda vieja en silencio si a
los filtering attributes les falta una columna.

**Una tabla espejo en Dataverse (`axx_ltmcustomer`) sincronizada a F&O.** Es el patrón de
Dual Write y tiene ventajas reales: un solo overlay en vez de dos, y los cuatro lookups a
las `mserp_ltm*` en una tabla en vez de duplicados en `contact` y `account`. Se descarta
porque **`LTMCustTable` es la única tabla de la localización que se escribe**: levantar un
modelo de datos paralelo para ocho campos no se amortiza. Además duplicaría el RUC, que ya
vive en `msdyn_identificationnumber` y ya lo consumen el `RucValidatorControl` y
`SetRucValidationService`. **Si aparecen más tablas `LTM*` que haya que escribir, esta es
la alternativa a retomar.**

## Consecuencias

**Más fácil:** los reintentos de `LTMCustTable` quedan aislados de `CustomersV3` — un
código de localización inválido no re-martilla el alta del customer. Y cuando se sume la
modificación, la cola y el consumidor ya están: entra un disparador nuevo y poco más.

**Más difícil:** hay una cola y un DLQ más que mirar, y el alta de un cliente pasa a
involucrar dos mensajes. Correlacionarlos es por `recordId`.

**Lo que queda sin cubrir:** un cambio de RUC, de tipo de documento o de dirección **no
llega a `LTMCustTable`**. La fila queda como se escribió en el alta hasta que se implemente
la modificación.

**Qué vigilar:**

- **Las virtual entities se activan una por una y por ambiente.** Si no están activadas —o
  si el application user de la Function no las puede leer— el lookup falla en runtime, no
  al arranque: el compilador no valida contra metadata a propósito.
- **El costo de leerlas.** Cada Retrieve sobre una virtual entity es un round-trip a F&O,
  encima de los que ya hace el sync, y las apps que llaman a F&O van con
  `maxInstanceCount = 1` (decisión #10). Por eso los dos catálogos se cachean por proceso
  en `LtmCatalogCache` — mismo patrón que `FoSchemaProvider`.
- **Las virtual entities suelen venir particionadas por company.** Un código elegido desde
  otra legal entity puede no existir en la company destino, y F&O lo rechaza con 400.
- **La dirección primaria.** `CountryRegionId` y `StateId` salen de `customeraddress` con
  `addressnumber = 1`. Un cliente sin esa dirección va igual, con los dos campos omitidos:
  si F&O los necesita, se entera con un 400 y no antes.

## Pendiente de documentar

- **Los nombres de las columnas dentro de las `mserp_ltm*`.** Las cuatro tablas están
  confirmadas; lo que falta son las columnas: `LtmCustMapping` las nombra siguiendo la
  convención del proveedor (`mserp_doctypeid`, `mserp_taxpayertypeid`,
  `mserp_accounttypegroupid`, `mserp_dataareaid`, `mserp_custvendentity`), pero ninguna se
  verificó contra la metadata de un ambiente.

