<!-- wiki-meta
sources:
  - docs/contracts/**
  - src/core/Axxon.Eip.Core/Messaging/**
  - infra/modules/servicebus.bicep
last_reviewed: 2026-08-21
-->

# Contratos de mensajería — Enterprise Integration Platform (EiP)

Este directorio define **cómo se comunican los sistemas a través del backbone
asincrónico** (Azure Service Bus). Es el prerequisito para conectar cualquier
satélite: fijar el contrato primero evita el "spaghetti" de formatos punto a punto
que la propuesta EiP busca eliminar.

## Archivos

| Archivo | Qué es |
|---|---|
| [eip-message-envelope.schema.json](../../contracts/eip-message-envelope.schema.json) | JSON Schema (draft 2020-12) del envelope estándar |
| [eip-message-envelope.example.json](../../contracts/eip-message-envelope.example.json) | Ejemplo de un mensaje válido |
| [dataverse-remote-execution-context.sample.json](../../contracts/dataverse-remote-execution-context.sample.json) | Muestra del formato **nativo** de Dataverse (interino — ver Estado actual) |

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
| Contacts (master matching) | dataverse | contact | Queue `contact-master-matching` (sessions) | **Interino**: formato nativo, ver abajo |
| Accounts (master matching) | dataverse | account | Queue `account-master-matching` (sessions) | **Interino**: formato nativo |
| Customers (qualify lead → F&O) | dataverse | customer | Queue (SB trigger) | **Interino** |
| Customers (legal entities fuera de Dual Write → F&O) | dataverse | account / contact | Queue `customer-fo-sync` (sessions por id de registro) | **Envelope EiP** — payload `CustomerSyncPayload` |
| CustomerGroups (F&O → Dataverse) | fo | customergroup | Timer (sin SB) | Pull batch, sin envelope |
| Products (F&O → Dataverse) | fo | product / productgroup | Timer/HTTP (sin SB) | Pull batch, sin envelope |

> Cada integración nueva debe agregar aquí su fila y, si define un payload propio,
> un archivo `{source}-{entityType}.schema.json` en este directorio.

## Estado actual vs objetivo

**Importante para el equipo.** Hoy el flujo de Contacts/Accounts **no** usa el
envelope EiP: Dataverse publica su `RemoteExecutionContext` nativo vía **Service
Endpoint** (ver [muestra](../../contracts/dataverse-remote-execution-context.sample.json)) y la
Function lo parsea con `ExecutionContextParser`. Ese formato:

- Es enorme y **específico de Dataverse** (InputParameters, PreEntityImages, ...);
  no sirve como contrato para satélites como Magento o los bancos.
- Acopla el consumer al modelo de ejecución de plugins de Dataverse.

Además, en el repo conviven **dos diseños del lado emisor** que hoy no coinciden:

1. **Service Endpoint nativo** (el que está vivo): publica el `RemoteExecutionContext`.
2. **`ContactEventPublisherPlugin` + `ServiceBusPublisher`** (código presente pero
   desconectado): publica un DTO limpio `ContactEventMessage`, que **no** es lo que
   `ExecutionContextParser` espera.

**Objetivo:** que todos los productores (incluido Dataverse, vía el plugin thin)
emitan el **envelope EiP** con el DTO de dominio en `payload`. Camino sugerido:

1. Nuevos satélites nacen ya con el envelope (source-agnostic) — sin deuda. El primero
   que lo cumple es `customer-fo-sync`: productor y consumidor son nuestros, así que no
   arrastra el formato nativo. Su payload es **una referencia, no un snapshot** — solo
   `recordId` y `dataAreaId`; el consumidor relee Dataverse. Un snapshot de un evento
   Update llega parcial (es un delta) y mapear desde ahí escribe mal en el ERP.
2. Unificar el lado Dataverse en **un** mecanismo: el plugin thin publica
   `EipMessage<ContactPayload>` (reusando el DTO limpio que ya existe) en lugar del
   Service Endpoint nativo.
3. Migrar `ExecutionContextParser` → deserializar el envelope. Se puede soportar
   ambos formatos en transición (detectar `schemaVersion`) y retirar el nativo
   cuando el plugin esté en producción.

Esto resuelve además la ambigüedad de diseño del punto 1 y deja un solo contrato
para las 12 integraciones.
