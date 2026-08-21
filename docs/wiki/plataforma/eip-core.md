<!-- wiki-meta
sources:
  - src/core/Axxon.Eip.Core/**
last_reviewed: 2026-08-21
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

