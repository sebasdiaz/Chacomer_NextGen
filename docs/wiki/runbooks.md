<!-- wiki-meta
sources_new:
  - docs/wiki/runbooks/*.md
last_reviewed: 2026-08-21
-->

# Runbooks

Qué hacer cuando pasa algo. Cada runbook arranca por el síntoma, no por el componente.

| Síntoma | Runbook |
|---|---|
| Un timer no ejecuta nunca, la app figura `Running` y no hay errores | [El timer no corre](runbooks/timer-no-corre.md) |
| Hay mensajes en la dead-letter queue | [Mensajes en la DLQ](runbooks/mensajes-en-dlq.md) |
| No llega telemetría a App Insights | [Telemetría › Dónde mirar](plataforma/telemetria.md#dónde-mirar) |
| Hay que dar de alta un ambiente nuevo | [Pipelines › Promoción del código a un ambiente nuevo](plataforma/pipelines.md#promoción-del-código-a-un-ambiente-nuevo) |
| El deploy de infra falla con `Authorization failed` | [Infraestructura › Deploy](plataforma/infraestructura.md#deploy) |

## Cómo trabajamos

| | |
|---|---|
| Un cambio, dos PRs (GitHub y Azure DevOps) | [Doble PR](runbooks/doble-pr.md) |
