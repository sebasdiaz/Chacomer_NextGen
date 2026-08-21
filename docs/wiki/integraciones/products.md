<!-- wiki-meta
sources:
  - src/integrations/products/**
  - pipelines/azure-pipelines-products.yml
last_reviewed: 2026-08-21
-->

# Products — F&O → Dataverse

Azure Function (.NET 10 isolated) con dos Timer Triggers que traen el maestro de artículos
de Finance & Operations hacia Dataverse. No consume ni publica en Service Bus.

## Las dos funciones

| Function | CRON | Lee de F&O | Escribe en Dataverse |
|---|---|---|---|
| `ProductGroupSyncFunction` | `%Schedules:ProductGroupSync%` — default `0 0 23 * * *` (diario 23:00) | `ProductGroups` | `msdyn_productgroup` |
| `ReleasedProductSyncFunction` | `%Schedules:ReleasedProductSync%` — default `0 0 * * * *` (cada hora) | `ReleasedProductsV2` | `msdyn_sharedproductdetails` |

`ProductGroupSyncService` hace **upsert** con `UpsertRequest` + `KeyAttributes` contra la
clave `msdyn_itemgroupid` + `msdyn_company`, en batches de `ExecuteMultiple` de 200 — el
mismo patrón que [customer groups](customergroups.md). La compañía se resuelve como lookup
a `cdm_company` por `cdm_companycode`.

> **El CRON va como `Schedules__ProductGroupSync` / `Schedules__ReleasedProductSync`, con
> doble guion bajo.** Si el nombre no resuelve, la app queda en `Running` sin ejecutar nada
> y sin error visible. Ver
> [Infraestructura › CRON de los timer triggers](../plataforma/infraestructura.md#cron-de-los-timer-triggers).

## Application Settings propios

| Setting | Descripción |
|---|---|
| `Schedules__ProductGroupSync` | CRON del sync de grupos. Default `0 0 23 * * *` |
| `Schedules__ReleasedProductSync` | CRON del sync de artículos. Default `0 0 * * * *` |
| `AssignOwningBusinessUnit` | Si es `true`, `ProductGroupSyncService` ejecuta `AssignRequest` para setear `owningbusinessunit`/`owningteam` con el team por defecto de la BU del `dataAreaId`. Requiere que ese team tenga `prvRead` sobre `msdyn_productgroup`. Default `false` |

Los settings de conexión (`DataverseUrl`, `FoBaseUrl`, y los `*ClientId`/`*ClientSecret` de
DESA) los provee [Axxon.Eip.Core](../plataforma/eip-core.md).

> **Es la única app que ya corre 100% por Managed Identity.** `main.bicep` la declara a
> propósito **sin** `dataverseAuthSettings` ni `foAuthSettings`: pasárselos le cambiaría el
> modo de autenticación de rebote. Es el estado deseado para las demás.

## Pendiente de documentar

- Mapeo campo a campo de `ReleasedProductsV2` → `msdyn_sharedproductdetails` y la estrategia
  de match que usa `SharedProductSyncService` (no usa `KeyAttributes`).
- Qué filtros aplica contra F&O y cómo se comporta ante un artículo cuya compañía no existe
  en Dataverse.
