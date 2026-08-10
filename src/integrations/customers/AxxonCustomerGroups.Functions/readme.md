# AxxonCustomerGroups.Functions

Azure Function (.NET 10 isolated) con Timer Trigger que sincroniza los grupos de
clientes de Finance & Operations (**CustomerGroups**) hacia la tabla
**msdyn_customergroup** de Dataverse, una vez por dia.

## Flujo

1. `CustomerGroupSyncFunction` corre segun el CRON `Schedules:CustomerGroupSync`
   (default `0 0 23 * * *` — todos los dias a las 23:00).
2. `FoCustomerGroupService` lee `GET {FoBaseUrl}/data/CustomerGroups?cross-company=true`
   con paginacion automatica (`@odata.nextLink`). Las legal entities listadas en
   `DualWriteLegalEntities` (ya sincronizadas por Dual Write) se excluyen
   server-side con `$filter=dataAreaId ne '...'`.
3. `CustomerGroupSyncService` hace upsert en `msdyn_customergroup` con
   `UpsertRequest` + `KeyAttributes` contra la **alternate key
   (msdyn_groupid + msdyn_company)** — Dataverse resuelve Create vs Update en el
   servidor — en batches de `ExecuteMultiple` de 200 requests
   (`ContinueOnError = true`: un registro fallido no corta el sync).
   Si la compania (`dataAreaId`) no existe en Dataverse el registro se omite
   con Warning, porque es parte de la clave.

> **Zona horaria:** el CRON corre en UTC salvo que la Function App tenga
> `WEBSITE_TIME_ZONE` configurado. Para las 23:00 de Asuncion, setear
> `WEBSITE_TIME_ZONE = "Paraguay Standard Time"`.

## Mapeo (CustomerGroups_Mapping.json, direccion AX -> CRM)

| CustomerGroups (F&O)            | msdyn_customergroup (Dataverse)        | Nota                                            |
|---------------------------------|----------------------------------------|-------------------------------------------------|
| `dataAreaId`                    | `msdyn_company`                        | Lookup `cdm_company` por `cdm_companycode`      |
| `CustomerGroupId`               | `msdyn_groupid`                        | Parte de la clave de upsert                     |
| `Description`                   | `msdyn_description`                    |                                                 |
| `IsSalesTaxIncludedInPrice`     | `msdyn_issalestaxincludedinprice`      | `yes` -> `true`, `no` -> `false`                |
| `PaymentTermId`                 | `msdyn_paymenttermid`                  | Lookup `msdyn_paymentterm` por `msdyn_name`     |
| `ClearingPeriodPaymentTermName` | `msdyn_clearingperiodpaymenttermname`  | Lookup `msdyn_paymentterm` por `msdyn_name`     |

Los payment terms se buscan primero por nombre **dentro de la misma compania**
(el nombre puede repetirse entre companias) y si no hay match se reintenta solo
por nombre. Si no existe, el campo se omite y se loguea Warning (el mapeo tiene
`createValuesOnDestination = false`: no se crean payment terms desde aca).

## Application Settings

| Setting                       | Descripcion                                                      |
|-------------------------------|------------------------------------------------------------------|
| `Schedules__CustomerGroupSync` | CRON del timer. Default: `0 0 23 * * *`. **Doble guion bajo**, ver abajo |
| `WEBSITE_TIME_ZONE`           | Zona horaria del CRON (ej. `Paraguay Standard Time`)             |
| `DualWriteLegalEntities`      | dataAreaIds excluidos del sync por estar en Dual Write, separados por coma (ej: `cha,cne`). Vacio = todas |
| `DataverseUrl`                | URL del environment de Dataverse                                 |
| `DataverseClientId`           | (DESA) Client Id del app registration; vacio => Managed Identity |
| `DataverseClientSecret`       | (DESA) Secret del app registration                               |
| `FoBaseUrl`                   | URL base del environment de F&O                                  |
| `FoTenantId`                  | (DESA) Tenant para client-credentials contra F&O                 |
| `FoClientId`                  | (DESA) Client Id; vacio => Managed Identity                      |
| `FoClientSecret`              | (DESA) Secret                                                    |
| `KeyVaultUri`                 | Vault del que se leen los secretos. En INTE: `https://keyvaultinte.vault.azure.net/` |
| `DataverseClientSecretName` / `FoClientSecretName` | Nombre del secret en el vault cuando no coincide con la clave. En INTE ambos: `SecretNextGenDynamics365Inte` |

> **El nombre del setting es `Schedules__CustomerGroupSync`, con doble guion bajo.** El
> binding pide `%Schedules:CustomerGroupSync%` (clave jerarquica) y el host mapea `__` a
> `:` al leer las variables de entorno. Escrito de cualquier otra forma
> (`SchedulesCustomerGroupSync`, `Schedules.CustomerGroupSync`) el placeholder no
> resuelve y el arranque falla asi:
>
> ```
> The 'CustomerGroupSyncFunction' function is in error:
>   '%Schedules:CustomerGroupSync%' does not resolve to a value.
> No job functions found.
> ```
>
> **La app queda "Running" y el timer no corre nunca.** No hay excepcion, no hay request
> fallido, no hay alerta: solo esos dos traces al iniciar el host. Para chequearlo sin
> esperar al horario del CRON: `GET /admin/functions/CustomerGroupSyncFunction/status`
> con la master key devuelve `{}` si indexo bien, o el error si no.

> En produccion usar Managed Identity de la Function App tanto para Dataverse
> (application user) como para F&O (registrar el client id en
> *System administration > Microsoft Entra applications*).
