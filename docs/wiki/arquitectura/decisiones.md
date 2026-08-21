<!-- wiki-meta
sources_new:
  - docs/wiki/arquitectura/decisiones/*.md
last_reviewed: 2026-08-21
-->

# Decisiones

Registro de las decisiones de diseño que **no se deducen leyendo el código**. La regla es
que cada una viva documentada en un solo lugar: si ya está explicada en su página, acá va
sólo el link.

## Registro

| # | Decisión | Dónde está el detalle |
|---|---|---|
| 1 | El envelope EiP es agnóstico a la fuente; el `payload` es lo único específico | [Mensajería › El envelope EiP](mensajeria.md#el-envelope-eip) |
| 2 | Un payload de `customer-fo-sync` es una **referencia**, no un snapshot: el consumidor relee Dataverse | [Mensajería › Estado actual vs objetivo](mensajeria.md#estado-actual-vs-objetivo) |
| 3 | Sólo `customer-fo-sync` lleva sessions; las que alimenta el Service Endpoint de Dataverse no pueden | [Infraestructura › Queues del namespace](../plataforma/infraestructura.md#queues-del-namespace) |
| 4 | Un 400 de F&O es permanente: va al DLQ sin reintentar. Los 401/403 sí se reintentan | [Customers › Manejo de errores](../integraciones/customers.md#manejo-de-errores-autocomplete--false) |
| 5 | El mapeo CRM → F&O vive en JSON (export de Dual Write intacto + overlay), no en C# | [Customers › Mapeo](../integraciones/customers.md#mapeo-por-json-no-hardcodeado) |
| 6 | Los mapeos se compilan al arranque y la app **no levanta** si alguno falla | [Customers › Validación](../integraciones/customers.md#validacion) |
| 7 | El flag de Dual Write ausente es `Unknown` y **no** sincroniza (no `false`) | [Customers › Las dos funciones](../integraciones/customers.md#las-dos-funciones-y-por-que-son-dos) |
| 8 | Un secret que no resuelve tira el host abajo a propósito, en vez de caer en silencio a MI | [Secretos y Key Vault](../plataforma/secretos-y-key-vault.md#cuando-el-secret-del-vault-se-llama-distinto) |
| 9 | Se usan los nombres canónicos de secret, no la indirección `{clave}Name` (que es transitoria) | [Secretos y Key Vault](../plataforma/secretos-y-key-vault.md) |
| 10 | Las apps que llaman a F&O van con `maxInstanceCount = 1`: el `maxConcurrentCalls` es por instancia | [Infraestructura › Scale-out](../plataforma/infraestructura.md#scale-out-y-límites-de-fo) |
| 11 | `FunctionAppLogs` al workspace es un backstop del host, no un duplicado de OpenTelemetry | [Infraestructura › Diagnostics](../plataforma/infraestructura.md#diagnostics-functionapplogs-al-workspace) |
| 12 | Si Thinkchat devuelve cero templates no se desactiva nada | [Thinkchat › Dos guardas de seguridad](../integraciones/thinkchat.md#dos-guardas-de-seguridad) |
| 13 | Se compila una vez y se promueve el mismo artifact INTE → TEST; no se recompila | [Pipelines](../plataforma/pipelines.md) |

## Cuándo escribir un ADR aparte

Cuando la decisión **no tiene una página natural donde vivir** — típicamente porque
atraviesa varias integraciones, porque descartó una alternativa que alguien va a volver a
proponer, o porque la vamos a revisar más adelante. En ese caso va un archivo en
[`decisiones/`](decisiones/), numerado, copiando
[`_template.md`](decisiones/_template.md).

Un ADR no se edita para cambiar de opinión: se escribe uno nuevo que lo reemplaza y el
viejo queda marcado como *Reemplazada por #N*. El registro es histórico.
