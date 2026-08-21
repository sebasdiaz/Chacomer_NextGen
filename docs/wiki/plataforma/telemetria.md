<!-- wiki-meta
sources:
  - src/core/Axxon.Eip.Core/Hosting/**
  - src/integrations/**/host.json
  - infra/modules/monitoring.bicep
last_reviewed: 2026-08-21
-->

# Telemetría

Las 5 apps exportan por **OpenTelemetry** a Application Insights. Son **dos procesos que
loguean por separado**, y confundirlos es la causa habitual de "no llega nada":

| | Qué emite | Dónde se configura |
|---|---|---|
| **Host** | inicio de la app, triggers, la tabla `requests` (una fila por invocación), salud del runtime | `logging.logLevel` de `host.json` |
| **Worker** | todo lo que loguea el código de las Functions y del core | `AddEipCore()` → `builder.Logging` |

Tres cosas que hay que tener presentes, las tres documentadas por Microsoft en
[Use OpenTelemetry with Azure Functions](https://learn.microsoft.com/azure/azure-functions/opentelemetry-howto):

1. **Los filtros de `host.json` no llegan al worker.** Poner ahí el namespace de la app
   (`"AxxonContacts": "Information"`) no hace nada; por eso los niveles del código viven
   en `AddEipCore()`.
2. **`logLevel.default` en `Warning` apaga la tabla `requests`.** El host escribe la
   telemetría de ejecución en nivel Information bajo `Host.Results`/`Function.<Nombre>`.
   Con `Warning` se filtra antes de salir, y quedás sin registro de las invocaciones.
   Por eso los 5 `host.json` van en `Information`.
3. **Con `telemetryMode: OpenTelemetry`, el bloque `logging.applicationInsights` se
   ignora** — sampling incluido. No tiene sentido configurarlo ahí; si hace falta
   samplear, va del lado de OTel.

Además, el host captura stdout y lo reenvía al pipeline: un `AddConsole()` en Azure
duplica cada log. `AddEipCore()` lo registra sólo fuera de Azure (`WEBSITE_INSTANCE_ID`
sin setear), donde es la única forma de ver la salida del worker.

> Con OpenTelemetry activo, el portal **no** soporta log streaming. Para ver qué está
> pasando se consulta App Insights.

### Dónde mirar

Un componente por ambiente, `appi-eip-{env}`, creado por el Bicep; las apps se distinguen
por `cloud_RoleName`. **No** hay que apuntarlas al App Insights de Dataverse
(`appinsightsdataverseinte` en INTE): ahí la plataforma escribe miles de
`Web API Request`/`Organization Service Request` por hora y la señal de las Functions
queda enterrada.

