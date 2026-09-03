<!-- wiki-meta
sources:
  - src/integrations/customers/AxxonCustomers.Functions/Mapping/Ltm*
  - src/integrations/customers/AxxonCustomers.Functions/Services/LtmCust*
last_reviewed: 2026-09-03
-->

# ADR-001 — LTMCustTable se sincroniza por cola propia, con payload de referencia

**Estado:** Aceptada
**Fecha:** 2026-08-25
**Revisada:** 2026-09-03 — el mapeo se verificó contra la metadata de INTE y de TEST, y
cuatro de las cadenas que venían del análisis funcional no existían. Ver
[Lo que dijo la metadata](#lo-que-dijo-la-metadata).

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
2. **Parte de sus campos salen de virtual entities de F&O** —
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
  para escribir la fila y para leer el catálogo de estados contra el que se valida `StateId`.
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

## Lo que dijo la metadata

El mapeo se escribió del análisis funcional y se dio por bueno sin contrastarlo con ningún
ambiente. El 2026-09-03 se verificó contra INTE y TEST, y **el lado de F&O estaba perfecto
mientras que el de Dataverse no coincidía en cuatro puntos**. Ninguna fila se podía escribir:
las altas de account fallaban en el `Retrieve` inicial y las de contact en la primera
consulta a los catálogos.

| Lo que asumía el análisis | Lo que hay | Consecuencia |
|---|---|---|
| `mserp_custvendentity` es un string y se filtra por `"Customer"` | Es un **Picklist** (200000000 Customer / 200000001 Vendor / 200000002 None) | La query no devuelve vacío: tira `System.FormatException ... Expected type of attribute value: System.Int32` |
| `axx_tipodocumento` es un lookup a `mserp_ltmtaxpayerdoctypeentity` | Es un **OptionSet local** (CI / RUC / Passport). Esa virtual entity no tiene ninguna relación 1:N — nadie la referencia | No hay `EntityReference` que leer, y la fila del catálogo se identifica por el **par (tipo de contribuyente, documento)**, no por el documento solo |
| El atributo se llama igual en las dos entidades | En `account` es **`axx_tipodedocumento`** | Estaba en el `ColumnSet`: toda alta de account fallaba antes de llegar al mapeo |
| `customeraddress.axx_pais` / `.axx_region` | **No existen.** `customeraddress` no tiene ningún lookup custom: país y región están en los campos OOB `country` (`"PY"`) y `stateorprovince`. Las tablas `axx_pais` y `axx_region` sí existen, pero cuelgan de otro árbol (país ← región ← localidad ← barrio) y nada las conecta con la dirección | La query de dirección pedía columnas inexistentes |
| La dirección primaria es la `addressnumber = 1` | Dataverse crea sola las direcciones 1 y 2 y casi nunca se completan; las cargadas a mano arrancan en la 3 | El filtro apuntaba justo a la fila vacía |

Los nombres que sí eran correctos: los nueve campos de `LTMCustTable` con su casing exacto,
la clave `(dataAreaId, AccountNum)`, las cuatro virtual entities `mserp_ltm*` (existen y
están activadas), y sus columnas `mserp_doctypeid`, `mserp_taxpayertypeid`,
`mserp_accounttypegroupid` y `mserp_dataareaid`.

**La lección para el próximo mapeo:** el análisis funcional se escribe desde el formulario y
nombra las tablas por lo que el negocio entiende, no por lo que el esquema declara. Un mapeo
que nunca se corrió contra un ambiente no está terminado, esté o no compilando.

## El alcance de la v1: documento RUC y Paraguay

Sobre esa verificación, el funcional acotó el alcance a **documento RUC y Paraguay**. Eso
simplifica el mapeo más de lo que parece: con el tipo de documento constante desaparece la
lectura de `axx_tipodocumento`, y con ella los dos problemas que traía (el OptionSet y el
nombre distinto en `account`).

| Campo | De dónde sale |
|---|---|
| `CountryDocTypeId` | Constante `"RUC"` |
| `CountryRegionId` | Constante `"PRY"` — el código de F&O, no el `"PY"` que guarda Dataverse |
| `TaxPayerTypeId` | Del tipo de registro: `PN` para contact, `PJ` para account |
| `AccountTypeGroupId` | `mserp_ltmaccounttypegroupentity` filtrando por company y `CustVendEntity = 200000000` |
| `StateId` | `customeraddress.stateorprovince` de la primera dirección que lo tenga, validado contra `AddressStates` de F&O |
| `CountryDocNum`, `StateDocNum` | `msdyn_identificationnumber` — el mismo RUC en los dos |
| `dataAreaId`, `AccountNum` | `msdyn_company` → `cdm_companycode`, y el campo de write-back |

Tres decisiones dentro del alcance que merecen su porqué:

**El tipo de contribuyente sale del tipo de registro y no de `axx_tipopersoneriajuridica`.**
Esa sería la fuente fiel al dato, pero está vacía en 99 de 142 contacts y en 139 de 142
accounts: la mayoría de los clientes quedaría sin el campo. Contact → `PN` y account → `PJ`
es además la misma decisión que ya tomaron los overlays de `CustomersV3`, que sellan
`PartyType` constante e inmutable (Person / Organization). Se confirma contra el catálogo de
la company antes de mandarlo.

**El alcance PY se deriva del ERP, no de una lista de companies en el repo.** Si la legal
entity no tiene filas de documento en `mserp_ltmtaxpayerdoctypeentity`, no se sincroniza: el
mensaje se completa sin procesar, igual que la guarda del `AccountNum`. No es un caso
marginal — en INTE la localización está configurada sólo en `chac`, `caut` y `bimo`, y **más
de la mitad de los clientes sellable viven en legal entities que no la tienen** (las de USA y
Alemania, entre otras). Sin la guarda todas ellas irían al DLQ, que dejaría de servir como
señal.

**El `StateId` se valida contra el catálogo de F&O y se omite si no matchea.** El
`stateorprovince` de Dataverse es texto libre y está sucio —conviven `DPTO_11`, `Asunción`,
`ASU`, `Central` y hasta `BA`—, y del lado de F&O los estados de PRY tienen la serie canónica
`DPTO_00`…`DPTO_17` más legados. Un estado que el ERP no conoce se rechaza con un 400 que
manda al DLQ la fila entera, por un campo que no es el objetivo de la integración.

**Cuando se amplíe el alcance** a CI y pasaporte hay que volver a leer el OptionSet del
cliente —con su nombre distinto en cada entidad— y traducirlo a los códigos del ERP
(`CI` → `CedID`, `Passport` → `PSP`). Si además se sale de Paraguay, `CountryRegionId` deja
de ser constante y hay que resolver de dónde sale un país que F&O reconozca, porque
`customeraddress.country` guarda `"PY"` y el ERP espera `"PRY"`.

## El mapeo no entra en el motor JSON

El mapeo trae cuatro cosas que las cinco primitivas de `FieldKind` no cubren:

| Campo | Cadena | Qué falta |
|---|---|---|
| `AccountTypeGroupId` | `mserp_ltmaccounttypegroupentity` donde company = X y `CustVendEntity` = Customer | No es navegación: es una consulta con filtro |
| `TaxPayerTypeId` | `mserp_ltmtaxpayerdoctypeentity` donde company = X y documento = RUC | Ídem, y encima el valor sale del tipo de registro y no de un atributo |
| `StateId` | `customeraddress.stateorprovince`, validado contra `AddressStates` de F&O | `customeraddress` es **1:N** (hay que elegir cuál dirección) y el valor se valida contra un catálogo del ERP |
| `CountryDocNum`, `StateDocNum` | `msdyn_identificationnumber` → dos campos | El motor indexa los mapeos **por atributo de CRM**: uno no puede alimentar dos campos |

> **Dos de las cuatro tablas de localización no aparecen en el código.**
> `mserp_ltmtaxpayertypeentity` y `mserp_ltmaddresscountryregionentity` existen y están
> activadas, pero con el alcance en RUC y Paraguay no hace falta consultarlas: el tipo de
> contribuyente sale de la fila de documento y el país es constante. Vuelven a entrar en juego
> cuando se amplíe el alcance.

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
- **Cuál dirección.** `StateId` sale de la primera dirección —por `addressnumber`— que tenga
  el campo cargado, no de la número 1. Un cliente sin ninguna va igual, con el campo omitido:
  si F&O lo necesita, se entera con un 400 y no antes.

- **El grupo de cliente ambiguo.** `caut` tiene **dos** filas Customer en
  `mserp_ltmaccounttypegroupentity` ("Cliente Local" y "Cliente Exterior"). No hay criterio en
  el repo para elegir —ordenarlas alfabéticamente elegiría la de exterior para clientes
  locales—, así que se omite el campo con las candidatas en el log y F&O aplica su default.
  Es la única pieza del mapeo que hoy queda sin resolver.

## Pendiente de definir con el funcional

- **Qué grupo de cliente le corresponde a `caut`.** Es lo único que impide un payload
  completo en esa legal entity, que además es la que más clientes tiene en INTE. Hasta que se
  decida, `AccountTypeGroupId` viaja omitido.

- **El RUC que se manda no siempre es un RUC.** Con el documento fijo en `"RUC"`, el
  `msdyn_identificationnumber` viaja etiquetado como tal aunque el cliente tenga cargada una
  cédula (en INTE hay varios: números de 8 dígitos, sin dígito verificador). El mapeo no lo
  puede distinguir y el ERP puede o no rechazarlo. Es el costo de acotar el alcance a RUC, y
  se resuelve cuando entren CI y pasaporte.

- **Nada de esto se ejerció contra F&O todavía.** El mapeo se verificó campo por campo contra
  la metadata y se simuló sobre datos reales de INTE, pero no se escribió ninguna fila: hasta
  que la app no esté desplegada, que F&O acepte el payload sigue siendo una hipótesis.

