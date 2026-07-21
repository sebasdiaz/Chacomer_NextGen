# Contratos de mensajería — Enterprise Integration Platform (EiP)

Este directorio define **cómo se comunican los sistemas a través del backbone
asincrónico** (Azure Service Bus). Es el prerequisito para conectar cualquier
satélite: fijar el contrato primero evita el "spaghetti" de formatos punto a punto
que la propuesta EiP busca eliminar.

## Archivos

| Archivo | Qué es |
|---|---|
| [eip-message-envelope.schema.json](eip-message-envelope.schema.json) | JSON Schema (draft 2020-12) del envelope estándar |
| [eip-message-envelope.example.json](eip-message-envelope.example.json) | Ejemplo de un mensaje válido |
| [dataverse-contact.schema.json](dataverse-contact.schema.json) | Payload de `source=dataverse, entityType=contact` |
| [dataverse-account.schema.json](dataverse-account.schema.json) | Payload de `source=dataverse, entityType=account` |
| [dataverse-remote-execution-context.sample.json](dataverse-remote-execution-context.sample.json) | Muestra del formato **nativo** de Dataverse (legacy — ver Estado actual) |

El envelope está tipado en código en `Axxon.Eip.Core/Messaging/EipMessage.cs`.

## El envelope EiP

Todo mensaje que viaja por Service Bus se envuelve en una estructura **agnóstica a
la fuente**: separa el "sobre" (routing, trazabilidad, idempotencia) del `payload`
(el DTO propio de cada entidad). Así el consumer maneja igual un evento de Dataverse,
de Magento o de un banco; solo cambia el `payload`.

| Campo | Obligatorio | Descripción |
|---|---|---|
| `schemaVersion` | sí | Versión del contrato (`"1.0"`) |
| `messageId` | sí | Único por mensaje — base de la **idempotencia** |
| `correlationId` | sí | Traza punta a punta (APIM → Function → D365), visible en App Insights |
| `source` | sí | Sistema origen: `dataverse`, `fo`, `magento`, `vtex`, ... |
| `entityType` | sí | `contact`, `customer`, `product`, ... |
| `operation` | sí | `create` \| `update` \| `delete` \| `upsert` |
| `occurredAt` | sí | Timestamp del evento en el origen (UTC, ISO 8601) |
| `partitionKey` | no | Clave de orden; se usa como **SessionId** cuando la queue tiene sessions |
| `metadata` | no | Diccionario libre (usuario iniciador, app origen, ...) |
| `payload` | sí | DTO específico de `source`+`entityType` |

## Convención de naming

| Elemento | Patrón | Ejemplo |
|---|---|---|
| Queue (orden garantizado) | `{entidad}-{proceso}` | `contact-master-matching` |
| Topic (varios consumidores) | `evt-{entidad}` | `evt-customer` |
| Subscription | `{consumidor}` | `magento` |
| `source` | minúsculas, sistema | `dataverse`, `magento` |
| `entityType` | minúsculas, singular | `contact`, `product` |

**Queue vs Topic:** queue con sessions cuando un solo consumidor necesita orden
(ej. `contact-master-matching`, ordenado por `msdyn_identificationnumber`). Topic
cuando el mismo evento le interesa a más de un consumidor (ej. "cliente actualizado"
→ Magento y Octopus).

## Manejo de errores (contrato interno de la plataforma)

Todo consumer sigue las mismas reglas — es lo que hace predecible a la plataforma:

| Situación | Acción | Razón de dead-letter |
|---|---|---|
| Payload no deserializable | **Dead-letter inmediato** (no reintentar) | `DeserializationFailed` |
| Faltan campos obligatorios del contrato | **Dead-letter inmediato** | `ContractViolation` |
| Violación de regla de negocio (reintentar no ayuda) | **Dead-letter** | `BusinessRuleFailed` |
| Error transitorio (red, throttling, lock) | **Abandon** → Service Bus reintenta hasta `maxDeliveryCount` → DLQ | — |

Constantes en `Axxon.Eip.Core/Messaging/EipConstants.cs` (`EipDeadLetterReason`).

- **Idempotencia:** el consumer debe tolerar entregas duplicadas (Service Bus es
  at-least-once). Usar `messageId` para deduplicar.
- **Orden:** cuando importa, la queue usa sessions y el productor setea
  `partitionKey` = SessionId (los eventos de una misma clave se procesan de a uno).

## Catálogo de contratos por integración

| Integración | source | entityType | Transporte | Estado |
|---|---|---|---|---|
| Contacts (master matching) | dataverse | contact | Queue `contact-master-matching` (sessions) | **Envelope** vía plugin thin; Function soporta envelope + legacy |
| Accounts (master matching) | dataverse | account | Queue `account-master-matching` (sessions) | **Envelope** vía plugin thin; Function soporta envelope + legacy |
| Customers (qualify lead → F&O) | dataverse | customer | Queue (SB trigger) | Interino |
| CustomerGroups (F&O → Dataverse) | fo | customergroup | Timer (sin SB) | Pull batch, sin envelope |
| Products (F&O → Dataverse) | fo | product / productgroup | Timer/HTTP (sin SB) | Pull batch, sin envelope |

> Cada integración nueva debe agregar aquí su fila y, si define un payload propio,
> un archivo `{source}-{entityType}.schema.json` en este directorio.

## Estado actual (emisor de Dataverse unificado)

El lado emisor de Contacts/Accounts quedó unificado en el **plugin thin**, que
emite el **envelope EiP** con el DTO de dominio en `payload`:

- `ContactEventPublisherPlugin` → envelope `entityType=contact`.
- `AccountEventPublisherPlugin` → envelope `entityType=account`.

Del lado consumidor, las Functions hacen **deserialización dual** (`EipEnvelopeParser`):
si el body trae `schemaVersion` → camino envelope (`EipMessage<T>`); si no → el
parser legacy del `RemoteExecutionContext` (`ExecutionContextParser` /
`AccountExecutionContextParser`). Esto permite convivir durante el rollout sin cortar
el flujo vivo.

### Rollout (pasos de Dataverse, fuera del repo)

1. Registrar los plugins (Create/Update en `contact` y `account`, Post-Op async) con
   su Secure Config `{connectionString}|{queueName}` apuntando a la queue de cada entidad.
2. Validar end-to-end en test (el envelope llega y se procesa).
3. **Deshabilitar el Service Endpoint nativo** de esas entidades. Reversible: si algo
   falla, se reactiva y la Function sigue procesando por el camino legacy.

### Deuda que queda

- El **legacy** (`RemoteExecutionContext` + los parsers + la
  [muestra](dataverse-remote-execution-context.sample.json)) se puede **retirar**
  cuando los plugins estén estables en producción y no queden mensajes legacy en vuelo.
- **Customers** (`QualifyLeadCustomerSyncFunction`) todavía no usa el envelope; es el
  siguiente candidato a unificar con el mismo patrón.

Nuevos satélites nacen directamente con el envelope (source-agnostic), sin pasar por
el formato nativo. Un solo contrato para las 12 integraciones.
