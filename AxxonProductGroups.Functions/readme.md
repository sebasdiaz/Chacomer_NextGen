# AxxonProductGroups.Functions

Timer Trigger que sincroniza **ProductGroups** desde Finance & Operations hacia **msdyn_productgroup** en Dataverse.

Integracion unidireccional: **F&O → Dataverse**.

## Mapeo de campos

| F&O (ProductGroups)  | Dataverse (msdyn_productgroup) | Tipo          |
|----------------------|-------------------------------|---------------|
| `dataAreaId`         | `msdyn_company`               | Lookup (cdm_company por cdm_companycode) |
| `GroupId`            | `msdyn_itemgroupid`           | String        |
| `GroupName`          | `msdyn_itemgroupname`         | String        |

La alternate key de upsert es **(msdyn_itemgroupid + msdyn_company)**.

## App Settings requeridos

| Setting                  | Descripcion                                              |
|--------------------------|----------------------------------------------------------|
| `DataverseUrl`           | URL del entorno Dataverse (ej. `https://org.crm.dynamics.com`) |
| `FoBaseUrl`              | URL base de F&O (ej. `https://env.operations.dynamics.com`) |
| `Schedules:ProductGroupSync` | CRON expression (ej. `0 0 23 * * *`)              |
| `DataverseClientId`      | (Solo DESA) App Registration Client ID                  |
| `DataverseClientSecret`  | (Solo DESA) App Registration Client Secret              |
| `FoTenantId`             | (Solo DESA) Tenant ID para F&O                          |
| `FoClientId`             | (Solo DESA) Client ID para F&O                          |
| `FoClientSecret`         | (Solo DESA) Client Secret para F&O                      |

En produccion usar **Managed Identity** — los settings `*ClientId` y `*ClientSecret` deben estar vacios.

## Ejecucion local

1. Copiar `local.settings.json.example` a `local.settings.json` y completar los valores.
2. `func start`
