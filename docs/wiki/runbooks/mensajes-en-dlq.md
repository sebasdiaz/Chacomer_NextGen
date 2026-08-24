<!-- wiki-meta
sources:
  - src/core/Axxon.Eip.Core/Messaging/**
last_reviewed: 2026-08-21
-->

# Runbook — mensajes en la DLQ

**Síntoma.** La dead-letter queue de alguna cola de `sb-chacomer-eip-{env}` tiene mensajes.

## 1. Leer el motivo antes de tocar nada

Cada mensaje dead-lettered trae `DeadLetterReason` y `DeadLetterErrorDescription`. El
motivo dice si el mensaje es **recuperable** o no:

| `DeadLetterReason` | Qué significa | ¿Sirve reprocesar? |
|---|---|---|
| `DeserializationFailed` | El body no se pudo deserializar al envelope EiP | No — el productor mandó mal |
| `ParseFailed` | El `RemoteExecutionContext` de Dataverse no se pudo parsear | No |
| `ContractViolation` | Faltan campos obligatorios (`entityType`, `recordId`, `msdyn_company`…) | No, hasta arreglar el origen |
| `BusinessRuleFailed` | F&O rechazó con 400/404/409/422. La descripción trae el **Infolog de F&O** | Sí, **después** de corregir el dato |
| `DataError` | El registro no existe o le falta un dato requerido en Dataverse | Sí, después de corregir el registro |
| `MaxDeliveryCountExceeded` | Se agotaron los reintentos por errores transitorios | Sí, si la causa transitoria pasó |

Las tres primeras son del contrato de la plataforma
(`EipDeadLetterReason` en `Axxon.Eip.Core/Messaging/EipConstants.cs`); `ParseFailed` y
`DataError` los agrega la integración de customers.

> Un `BusinessRuleFailed` **no** es un incidente de plataforma: el mensaje llegó bien y el
> ERP lo rechazó. La descripción trae el motivo real (un customer group que no existe en la
> compañía, un party que ya existe como prospect). Reintentarlo sin corregir el dato vuelve
> a fallar igual.

## 2. Correlacionar con App Insights

Todo consumer loguea antes de dead-letter. Con el `messageId` o el `correlationId`:

```
traces | where cloud_RoleName == "AxxonCustomers.Functions" | where message contains "<messageId>" | order by timestamp desc
```

Si no aparece nada, revisar que la app esté efectivamente emitiendo telemetría — ver
[Telemetría](../plataforma/telemetria.md).

## 3. Reprocesar

Los consumers son **idempotentes por diseño** (Service Bus es at-least-once y el
`messageId` es la base de la deduplicación), así que reenviar un mensaje corregido es
seguro. Se hace desde el portal de Service Bus con *Receive → Resend*, o con
`az servicebus` sobre la subcola `$deadletterqueue`.

**Antes de reprocesar en masa**, confirmar que la causa está resuelta: un lote reenviado
contra la misma causa vuelve a la DLQ y consume delivery count de nuevo.

## Pendiente

- No hay **DLQ handler automático**: hoy el drenaje es manual. Está fuera del alcance de V1.
- No hay alerta configurada sobre la profundidad de la DLQ.
