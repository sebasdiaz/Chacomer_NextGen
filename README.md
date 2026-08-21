# Chacomer NextGen — Enterprise Integration Platform (EiP)

Integraciones entre **Dynamics 365 (Dataverse)**, **Finance & Operations** y los satélites
externos: Azure Functions .NET 10 sobre un backbone de Azure Service Bus, con la
infraestructura en Bicep y un pipeline por integración.

## 📖 La documentación está en la wiki

**→ [`docs/wiki/`](docs/wiki/README.md)**

| Para saber | Ir a |
|---|---|
| Qué es la EiP y cómo está organizado el repo | [Visión general](docs/wiki/arquitectura/vision-general.md) |
| Qué hace cada integración | [Integraciones](docs/wiki/integraciones.md) |
| Dónde corre y cómo se despliega | [Ambientes](docs/wiki/plataforma/ambientes.md) · [Pipelines](docs/wiki/plataforma/pipelines.md) |
| Qué hacer cuando algo falla | [Runbooks](docs/wiki/runbooks.md) |
| Por qué está hecho así | [Decisiones](docs/wiki/arquitectura/decisiones.md) |

> **La wiki se actualiza en el mismo PR que el código.** Cada página declara de qué código
> depende en un bloque `wiki-meta`; si tu cambio toca esos archivos, la página se revisa
> junto con el cambio. Ver [Cómo se mantiene](docs/wiki/README.md#cómo-se-mantiene).

## Build y tests

```bash
dotnet build Chacomer.sln -c Release
```

```bash
dotnet test tests/AxxonCustomers.Functions.Tests/AxxonCustomers.Functions.Tests.csproj
```

El plugin de Dataverse necesita el Strong Name Key, una sola vez:

```powershell
.\generate-snk.ps1
```

## Estructura

```
src/core/            Axxon.Eip.Core — lo que comparten todas las Function Apps
src/integrations/    una carpeta por dominio (contacts, customers, fiscal, products, thinkchat)
src/webresources/    controles PCF
infra/               Bicep — un despliegue por ambiente
pipelines/           Azure Pipelines — uno por integración + infra
docs/wiki/           la wiki
docs/contracts/      contratos de mensajería (JSON Schema)
tests/               xUnit
```

## Contribuir

Cada cambio va en **dos PRs**, uno en GitHub y otro en Azure DevOps —
ver [Doble PR](docs/wiki/runbooks/doble-pr.md).
