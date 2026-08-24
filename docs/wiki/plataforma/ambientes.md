<!-- wiki-meta
sources:
  - infra/environments/**
  - infra/scripts/**
  - pipelines/**
last_reviewed: 2026-08-24
-->

# Ambientes

## Ambientes

**Son dos resource groups, y sólo dos.** Ambos en la suscripción
`AZURE_DYNAMICS` (`09592883-…`), tenant `d0e6feed-…`, región `eastus`.

| Ambiente | Resource group | Dataverse | F&O | Service connection | Function Apps |
|---|---|---|---|---|---|
| `inte` | `DataverseINTE` | `operations-b1-chacomer-inte` | `b1-chacomer-inte.sandbox` | `sc-chacomer-eip-inte` | fuera del Bicep (ver cutover), salvo thinkchat |
| `test` | `dataversetest` | `operations-b1-chacomer-test` | `b1-chacomer-test.sandbox` | `sc-chacomer-eip-test` | administradas por el Bicep |
| `uat` | *(sin crear)* | — | — | — | — |
| `prod` | *(sin crear)* | — | — | — | — |

Los RG `EiP_Inte` y `EiP_Test` fueron un intento anterior y se descartan.

Ambos RG ya contenían recursos legacy hechos a mano (`appinsightstest` y
`keyvaultchacomertest` en test; `keyvaultinte`, `appinsightsdataverseinte` y
otros en INTE). No colisionan con los nombres del Bicep (`appi-eip-{env}`,
`kv-chacomer-eip-{env}`) y quedan fuera de este template.

## Cutover de INTE

`dataversetest` arranca en verde: no tiene Function Apps, así que el Bicep corre
completo. `DataverseINTE` no: tiene 4 apps vivas creadas a mano, y por eso
`inte.bicepparam` va con **`deployFunctionApps = false`**. Hoy el Bicep
administra en INTE sólo los recursos compartidos.

Las apps existentes ya son **FC1 / Flex Consumption Linux**, igual que las que
crea el template — la diferencia no es la infraestructura sino la configuración:

| Aspecto | Hoy en `DataverseINTE` | Lo que declara el Bicep |
|---|---|---|
| Storage | `AzureWebJobsStorage` + `DEPLOYMENT_STORAGE_CONNECTION_STRING` (connection string) | `AzureWebJobsStorage__blobServiceUri` + MI |
| Service Bus | `ServiceBusConnection` (connection string) | `__fullyQualifiedNamespace` + MI |
| Managed Identity | sólo `fa-axxoncontacts-inte` la tiene | System-Assigned en las 5 |

> **`fa-axxonticketatencion-inte` es la quinta app de INTE creada a mano** y entra en el
> mismo cutover. Ya tiene System-Assigned MI y el rol `Key Vault Secrets User` sobre
> `keyvaultinte`; le falta subir el runtime de `dotnet-isolated 8.0` a `10.0`. Ver
> [Ticket de Atención › Estado del despliegue](../integraciones/ticketatencion.md#estado-del-despliegue).
| Secretos | Key Vault `keyvaultinte` (ver abajo) | Key Vault `kv-chacomer-eip-inte` |
| App Service Plan | `ASP-DataverseINTE-*` (uno por app) | `asp-{functionAppName}` |
| Settings extra | `Schedules*`, `DataverseClientId`, `FoClientId`, `FoTenantId` | no contemplados |

El template declara la **colección completa** de app settings, así que poner
`deployFunctionApps = true` sin preparar el terreno **deja las 4 apps caídas**.
El orden del cutover, por app:

> **`fa-axxonthinkchat-{env}` es la excepción.** Es la única app greenfield: no existe
> creada a mano en ningún ambiente, así que no tiene nada que adoptar y puede nacer
> administrada por Bicep sin esperar al cutover. Por eso tiene su propio toggle
> `deployThinkchatApp` (default: sigue a `deployFunctionApps`). Para prenderla en INTE
> hace falta que el SP del pipeline pueda escribir role assignments — ver abajo.

1. ✅ **Secretos a Key Vault + System-Assigned MI** — [`infra/scripts/Set-InteKeyVaultAuth.ps1`](../../../infra/scripts/Set-InteKeyVaultAuth.ps1).
2. Dar de alta la MI como Application User en Dataverse y como usuario S2S en F&O.
3. Agregar los settings faltantes (`Schedules*`) al `appSettings` del módulo.
4. Resolver `fa-axxoncustomergroup` → `fa-axxoncustomergroups-inte`, y el plan
   (una app Flex no se mueve entre planes: hay que recrearla o adoptar el plan
   existente en el template).
5. Apuntar `KeyVaultUri` a `keyvaultinte` en el módulo (ver abajo).
6. Recién ahí, `deployFunctionApps = true`.

Mientras tanto los pipelines de integración siguen deployando código a las apps
de INTE tal como están, vía los overrides `inteAppName` / `deployToInte`.

### Secretos de INTE: `keyvaultinte`, no `kv-chacomer-eip-inte`

INTE lee sus secretos del vault legacy **`keyvaultinte`**, que ya existía en el RG y ya
tiene cargado el client secret del app registration `NextGen_Dynamics365_Inte`
(`145fd64d-…`) bajo el nombre `SecretNextGenDynamics365Inte`. Se usa ese y no
`kv-chacomer-eip-inte` para no duplicar el mismo secreto en dos vaults.

Como el nombre del secret no coincide con la clave de configuración, las apps llevan la
indirección `DataverseClientSecretName` / `FoClientSecretName` (ver
[Secretos y Key Vault › Cuando el secret del vault se llama distinto](secretos-y-key-vault.md#cuando-el-secret-del-vault-se-llama-distinto)).

El cutover lo aplica un script idempotente — las apps de INTE están fuera del Bicep, así
que su configuración no puede versionarse en el template:

```powershell
# 1. Dry run
./infra/scripts/Set-InteKeyVaultAuth.ps1 -WhatIf

# 2. App por app: MI + rol sobre el vault + los *SecretName, sin borrar nada
./infra/scripts/Set-InteKeyVaultAuth.ps1 -Apps fa-axxoncontacts-inte

# 3. Validada la app, se borran los secretos planos
./infra/scripts/Set-InteKeyVaultAuth.ps1 -Apps fa-axxoncontacts-inte -RemovePlainSecrets
```

> **Antes del paso 2 en `fa-axxoncustomergroup`:** esa app venía con otro app registration
> (`NextGenInte`, `adcf4b4d-…`) y el script la unifica en `NextGen_Dynamics365_Inte`. Hay que
> confirmar primero que ese registration esté dado de alta como Application User en
> Dataverse INTE y como usuario S2S en F&O con permisos sobre customer groups. Si no,
> correrla con `-SkipClientIdUnification`.

Cuando INTE pase a `deployFunctionApps = true`, `functionApp.bicep` cablea el
`keyVaultUri` del vault que crea el propio template: hay que parametrizarlo para que INTE
siga apuntando a `keyvaultinte`, o migrar los secretos a `kv-chacomer-eip-inte`.

### INTE: thinkchat con los roles a mano

`inte.bicepparam` va con **`deployRoleAssignments = false`** por el mismo motivo, con el
SP `b391d418-…` (`sc-chacomer-eip-inte`, objectId `e57cb312-…`), que tiene Contributor
sobre `DataverseINTE` pero no `roleAssignments/write`. Como en INTE
`deployFunctionApps = false`, la única app en juego es `thinkchat`.

La app nace sin roles, y **sin ellos no arranca**: `AzureWebJobsStorage` va por identidad.
Después de cada deploy que la (re)cree, correr:

```bash
RG=DataverseINTE
APP=fa-axxonthinkchat-inte
MI=$(az functionapp show -g $RG -n $APP --query identity.principalId -o tsv)
ST=$(az storage account list -g $RG --query "[?starts_with(name,'stthinkchatinte')].id | [0]" -o tsv)
KV=$(az keyvault show -n kv-chacomer-eip-inte --query id -o tsv)

az role assignment create --assignee-object-id $MI --assignee-principal-type ServicePrincipal --role "Storage Blob Data Owner"          --scope $ST
az role assignment create --assignee-object-id $MI --assignee-principal-type ServicePrincipal --role "Storage Queue Data Contributor"   --scope $ST
az role assignment create --assignee-object-id $MI --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User"           --scope $KV
```

Ninguno de esos tres roles cae en la condición ABAC que restringe a `sebastian.diaz@`
(sólo le niega `Owner`, `User Access Administrator` y `Role Based Access Control
Administrator`), así que este paso no depende de nadie más.

Falta además, del lado de Dataverse: la MI de la app tiene que estar dada de alta como
**Application User en Dataverse INTE** con permisos sobre `axx_metatemplates`. La app va
sin `dataverseAuthSettings` a propósito (mismo criterio que `products`): habla con
Dataverse por managed identity, así que sin ese alta levanta pero el sync falla.

