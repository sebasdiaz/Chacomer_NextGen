<!-- wiki-meta
sources:
  - src/core/Axxon.Eip.Core/**
last_reviewed: 2026-08-31
-->

# Axxon.Eip.Core — componentes cross

Toda Function App de la plataforma referencia `Axxon.Eip.Core` y arranca igual:

```csharp
var builder = FunctionsApplication.CreateBuilder(args);

builder.AddEipCore();                                    // Key Vault + OpenTelemetry/App Insights + logging
builder.Services.AddEipDataverse(builder.Configuration); // IOrganizationService via MI o Client Secret
builder.Services.AddEipFoOData(builder.Configuration);   // cliente OData de F&O con retry 429/Retry-After

// ... servicios propios del dominio ...

builder.Build().Run();
```

Qué provee el core:

| Componente | Descripción |
|---|---|
| `AddEipCore()` | Key Vault como fuente de secretos (si `KeyVaultUri` está seteado), OpenTelemetry exportando a App Insights, niveles de log del worker |
| `AddEipDataverse()` | `DataverseClientFactory` — Managed Identity en Azure, Client Secret en DESA/local |
| `AddEipFoOData()` | `IFoODataClient` — cliente genérico de la OData API de F&O: paginación `@odata.nextLink`, `cross-company`, `$filter`/`$select`, POST tipado, y retry SOLO ante HTTP 429 respetando `Retry-After` |
| `AddEipDataverseWebApi()` | `IDataverseWebApiClient` — acceso OData a Dataverse, para las consultas que no se expresan bien en FetchXML (`$expand` anidados, `$orderby`+`$top`). Convive con `AddEipDataverse()` y comparte `DataverseOptions`. Un error HTTP lanza `DataverseWebApiException`: nunca degrada el resultado en silencio |
| `AddEipGraph()` | `IGraphSharePointService` — subir archivos a una biblioteca de un sitio, convertir Office → PDF y borrar. Lo usa [Ticket de Atención](../integraciones/ticketatencion.md). Los permisos son de **aplicación** (`Sites.ReadWrite.All`) y necesitan consentimiento de admin. **`driveId` no es opcional por comodidad**: sin él se escribe en la biblioteca por defecto del sitio, y las de Dataverse son otras — ver [dónde va el PDF](../integraciones/ticketatencion.md#donde-va-el-pdf-una-biblioteca-por-tabla-no-una-carpeta) |
| `EipCredentialFactory` | Criterio único de auth contra Entra: con `ClientId` + `ClientSecret` va Service Principal, sin ellos `DefaultAzureCredential` (Managed Identity en Azure). El credential se registra **singleton** — Azure.Identity cachea los tokens por instancia |

