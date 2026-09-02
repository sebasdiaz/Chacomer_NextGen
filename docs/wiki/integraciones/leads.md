<!-- wiki-meta
sources:
  - src/integrations/leads/**
  - tests/AxxonLeads.Functions.Tests/**
  - docs/contracts/lead-intake.schema.json
  - pipelines/azure-pipelines-leads.yml
last_reviewed: 2026-08-27
-->

# Leads — alta desde satélites

Function App (.NET 10 isolated) que crea **leads en Dataverse** a partir de la cola
`lead-intake`. La alimentan los sistemas satélite: Thinkchat, el sitio web, formularios de
campaña. Una sola function:

| Function | Trigger | Qué hace |
|---|---|---|
| `LeadIntakeFunction` | Service Bus, cola `lead-intake` (sin sessions) | Valida el contrato y crea el `lead` |

## Por qué una cola y no un endpoint HTTP

El satélite **no habla con Dataverse**: manda un mensaje y se desentiende. Eso compra tres
cosas que un POST directo no da:

- **Dataverse puede estar caído** y no se pierde un lead. El mensaje espera en la cola.
- **El satélite no conoce los logical names** (`msdyn_identificationnumber`,
  `address1_stateorprovince`). Cumple un contrato estable y los cambios de esquema no lo
  tocan.
- **El throttling de Dataverse deja de ser problema del satélite.** El techo de escritura lo
  pone `maxConcurrentCalls: 1` en el `host.json` de la app: una escritura por instancia.

Es también la primera integración cuyo productor **no es nuestro**. De ahí las dos
decisiones que siguen.

## La cola: sin sessions, con detección de duplicados

**Sin sessions**, a diferencia de `customer-fo-sync` y `customer-ltm-sync`: cada mensaje
crea un lead independiente y no hay dos eventos del mismo registro que puedan cruzarse.
Poner sessions serializaría todo el intake sin que nada lo necesite.

**Con detección de duplicados** (`requiresDuplicateDetection`, ventana de 1 h) — la única
cola del namespace que la tiene, justamente porque es la única que alimentan terceros: un
satélite que no recibe el ACK reintenta el envío, y sin esto cada reintento sería un lead
duplicado. Es inmutable: prenderla en una cola ya creada obliga a recrearla.

Eso cubre el reenvío con el mismo `messageId`. **No** cubre el otro caso: el `Create` sale
bien y después se pierde el lock, Service Bus reentrega y se crea un segundo lead. Ese lo
cierra la deduplicación contra Dataverse — ver abajo.

## Deduplicación contra Dataverse (hoy apagada)

`LeadIntakeService` busca el lead por el id del sistema origen (`externalId` del payload)
antes de crear. Necesita una **columna en `lead` donde guardarlo**, y el logical name de esa
columna sale del app setting `LeadExternalIdAttribute`.

**Hoy está vacío en INTE y en TEST**: la columna no existe, así que la deduplicación está
apagada y la única protección es la de la cola. Para cerrarlo:

1. Crear una columna de texto en `lead` (ej. `axx_externalid`), idealmente con **alternate
   key** para que el índice haga la búsqueda barata.
2. Poner su logical name en `leadExternalIdAttribute` en el `.bicepparam` del ambiente y
   redeployar la infra. No hace falta tocar código.

Con la columna vacía, `LeadEntityBuilder` tampoco intenta escribir el `externalId` — escribir
en una columna inexistente voltearía el `Create` entero.

## Campos obligatorios

Se validan en `LeadEnvelopeValidator` **antes** de tocar Dataverse. No se delega al SDK a
propósito: su error ante un campo requerido faltante es genérico y no dice cuál falta, y el
satélite necesita esa respuesta en la razón del dead-letter para poder corregir.

| Campo del payload | Columna | Regla |
|---|---|---|
| `subject` | `subject` | Obligatorio |
| `identificationNumber` | `LeadIdentificationAttribute` | Obligatorio |
| `lastName` **o** `companyName` | `lastname` / `companyname` | Al menos uno |

`lastName` o `companyName`, y no los dos, porque depende de si el satélite captó a una
persona o a un comercio. Obligar a los dos haría que alguien invente el que falta.

> ⚠️ **`LeadIdentificationAttribute` no está verificado contra el org.** El default
> (`msdyn_identificationnumber`) es el mismo campo que usa el master matching de
> [contacts](contacts.md), pero **no se confirmó que exista en la tabla `lead`** de INTE ni
> de TEST. Si el logical name real es otro, se corrige en el `.bicepparam` del ambiente —
> no en el código. Mientras esté mal, cada lead termina en el DLQ con un mensaje que dice
> exactamente eso y nombra el app setting a corregir.

## Domicilio

Va en los campos nativos `address1_*` del lead, no en una tabla relacionada. Dos motivos:

- Se escribe en el **mismo `Create`** que el lead: no hay un segundo registro que pueda
  quedar huérfano si la segunda escritura falla.
- Cuando el lead se **califica**, Dataverse arrastra esos campos al contact/account que
  crea. Un domicilio en una tabla propia habría que volver a copiarlo a mano.

`address` es opcional y **parcial**: lo que no viene no se escribe. Esa es la regla de todo
el mapeo, no sólo de la dirección — ver `LeadEntityBuilder`. En un `Create` da igual mandar
un string vacío, pero el día que esto soporte `Update` la diferencia entre "no vino" y
"vino vacío" es la diferencia entre respetar y borrar un dato que ya estaba.

## Contrato del mensaje

Envelope EiP estándar con `LeadIntakePayload` en `payload`. Schema y ejemplo completo:
[`lead-intake.schema.json`](../../contracts/lead-intake.schema.json) ·
[`lead-intake.example.json`](../../contracts/lead-intake.example.json).
El envelope en sí está explicado en [Mensajería](../arquitectura/mensajeria.md).

## Manejo de errores

Sigue el [contrato de errores de la plataforma](../arquitectura/mensajeria.md#manejo-de-errores-contrato-interno-de-la-plataforma):

| Situación | Acción | Razón de dead-letter |
|---|---|---|
| El cuerpo no deserializa al envelope | DLQ inmediato | `DeserializationFailed` |
| Falta un obligatorio del lead | DLQ inmediato, con el campo en la descripción | `ContractViolation` |
| Columna del mapeo inexistente en el org | DLQ inmediato, nombrando el app setting | `BusinessRuleFailed` |
| Dataverse rechaza por dato inválido (optionset, longitud) | DLQ inmediato | `BusinessRuleFailed` |
| Dataverse throttlea o timeoutea | Abandon → reintento → DLQ tras `maxDeliveryCount` | — |

La distinción del último caso es deliberada: Dataverse contesta con `FaultException` tanto
por un dato inválido como por estar sobrecargado. `LeadIntakeFunction.IsPermanent` deja
pasar a reintento los códigos de protección del servicio (`NumberOfRequestsExceeded`,
`TimeLimitExceeded`, `ConcurrencyLimitExceeded`, `SqlTimeout`) — mandarlos al DLQ perdería
leads por una ventana de throttling.

Qué hacer con lo que quedó en el DLQ: [Mensajes en DLQ](../runbooks/mensajes-en-dlq.md).

## Autenticación

**Managed Identity**, como [fiscal](fiscal.md) y [thinkchat](thinkchat.md): el `.bicepparam`
no declara `dataverseClientId` para esta app. Requiere dos altas que el Bicep no puede hacer:

1. La MI como **Application User en Dataverse**, con permiso de **creación sobre `lead`**
   (y de lectura, si se prende la deduplicación). Sin eso, todos los mensajes van al DLQ.
2. Los roles de Azure sobre Storage, Key Vault y **Azure Service Bus Data Receiver** —
   sin este último el trigger no puede leer la cola. En INTE y TEST se asignan a mano
   porque los dos ambientes van con `deployRoleAssignments = false`: ver
   [Ambientes](../plataforma/ambientes.md).

## Pendiente de documentar

- **El logical name real del RUC/cédula en `lead`.** Ver la advertencia de arriba.
- **Qué satélite estrena la cola y con qué `source`.** El diseño asume Thinkchat primero,
  pero no hay todavía un productor escrito: hoy la cola sólo se puede probar encolando a
  mano un mensaje con la forma de
  [`lead-intake.example.json`](../../contracts/lead-intake.example.json).
- **Los valores de `leadsourcecode`** que usa el negocio, para que cada satélite mande el
  suyo. Viajan sin traducir a propósito: mapearlos en la Function obligaría a redeployar
  cada vez que se agrega una opción.
- **Qué pasa después del alta.** Hoy el lead se crea y ahí termina el flujo: la calificación
  y el pase a contact/account los sigue haciendo el proceso de Dataverse
  ([customers](customers.md) toma la posta desde `QualifyLead`).
