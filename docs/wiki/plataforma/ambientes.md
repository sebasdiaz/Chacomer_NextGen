<!-- wiki-meta
sources:
  - infra/environments/**
  - infra/scripts/**
  - pipelines/**
last_reviewed: 2026-09-02
-->

# Ambientes

## Ambientes

**Son dos resource groups, y sólo dos.** Ambos en la suscripción
`AZURE_DYNAMICS` (`09592883-…`), tenant `d0e6feed-…`, región `eastus`.

| Ambiente | Resource group | Dataverse | F&O | Service connection | Function Apps |
|---|---|---|---|---|---|
| `inte` | `DataverseINTE` | `operations-b1-chacomer-inte` | `b1-chacomer-inte.sandbox` | `sc-chacomer-eip-inte` | fuera del Bicep (ver cutover), salvo thinkchat, ticketatencion, fiscal, customerdata y customercredit |
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
| Secretos | Key Vault `keyvaultinte` (ver abajo) | Key Vault `kv-chacomer-eip-inte` |
| App Service Plan | `ASP-DataverseINTE-*` (uno por app) | `asp-{functionAppName}` |
| Settings extra | `Schedules*`, `DataverseClientId`, `FoClientId`, `FoTenantId` | no contemplados |

El template declara la **colección completa** de app settings, así que poner
`deployFunctionApps = true` sin preparar el terreno **deja las 4 apps caídas**.
El orden del cutover, por app:

> **Mientras tanto, lo que el Bicep agrega para INTE hay que ponerlo a mano.** Hoy es
> `MasterOwnerTeamName = CLIENTE UNICO` en `fa-axxoncontacts-inte`: sin ese setting los
> masters se siguen creando, pero fuera de la business unit CLIENTE UNICO. El param ya está
> en `inte.bicepparam` para que el día del cutover no haya que acordarse. Ver
> [Contacts › Owner del master](../integraciones/contacts.md#owner-del-master-la-business-unit-cliente-unico).

> **`fa-axxonthinkchat-{env}` fue la primera excepción.** Es una app greenfield: no existe
> creada a mano en ningún ambiente, así que no tiene nada que adoptar y puede nacer
> administrada por Bicep sin esperar al cutover. Por eso tiene su propio toggle
> `deployThinkchatApp` (default: sigue a `deployFunctionApps`). Para prenderla en INTE
> hace falta que el SP del pipeline pueda escribir role assignments — ver abajo.

> **`fa-axxonfiscal-inte` sigue el mismo camino.** También es greenfield —nunca se creó a
> mano— así que estrena en INTE con su propio toggle `deployFiscalApp`, sin esperar al
> cutover. Nace con **Managed Identity**: `inte.bicepparam` no declara `dataverseClientId`,
> así que habla con Dataverse por su MI. Ver
> [Fiscal › Estado del despliegue](../integraciones/fiscal.md#estado-del-despliegue).

> **`fa-axxoncustomerdata-inte` es la última de la serie.** Greenfield igual que las
> anteriores: estrena en INTE con su toggle `deployCustomerDataApp` y Managed Identity. En
> **TEST el toggle está explícitamente en `false`** — a diferencia de fiscal y thinkchat, que
> ahí se crean por default— para que la app no aparezca de rebote antes de que alguien
> decida promoverla. Ver
> [Customer data › Estado y despliegue](../integraciones/customerdata.md#estado-y-despliegue).

> **`fa-axxoncustomercredit-inte` sigue el mismo molde, pero todavía no existe.** Greenfield,
> toggle propio `deployCustomerCreditApp` (ya en `true` en `inte.bicepparam`, en `false` en
> TEST) y Managed Identity. La diferencia con las anteriores: su MI no se da de alta en
> Dataverse sino **en F&O**, con lectura sobre las cuatro entidades `DevAxCustCredit*`. El
> orden en que se prenden las cosas está en
> [Customer credit › Cómo se estrena](../integraciones/customercredit.md#cómo-se-estrena).

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

> **`fa-axxonticketatencion-inte` salió del cutover.** Era la quinta app creada a mano; se
> borró, así que —como thinkchat— nace administrada por Bicep con su propio toggle
> `deployTicketAtencionApp`. Es la única app del ambiente que autentica con **dos
> identidades**: Managed Identity contra Dataverse y el app registration `145fd64d` contra
> Graph, porque el consentimiento de `Sites.ReadWrite.All` quedó sobre el registration. Su
> secreto no se duplica en el vault: sale del `DataverseClientSecret` que ya está cargado,
> por la indirección `graphClientSecretName`. Los pasos de alta están en
> [Ticket de Atención › Estado del despliegue](../integraciones/ticketatencion.md#estado-del-despliegue).
>
> Desde el 2026-08-31 **también se despliega en TEST**, con `deployTicketAtencionApp` y
> `deployToTest` prendidos a la vez. Ahí la identidad es más simple —el app registration
> compartido para Dataverse y para Graph—, pero quedan cuatro altas manuales del lado de
> Dataverse y de Azure: están en
> [Lo que el Bicep no puede hacer al promover a TEST](../integraciones/ticketatencion.md#lo-que-el-bicep-no-puede-hacer-al-promover-a-test).

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

> **Resuelto.** Esto decía que al pasar INTE a `deployFunctionApps = true` había que
> parametrizar el `keyVaultUri` para que siguiera apuntando a `keyvaultinte`. Ya no hace
> falta: verificado el 2026-08-24, **`kv-chacomer-eip-inte` tiene los 8 secretos**, migrados
> el 2026-08-12 — incluido `SecretNextGenDynamics365Inte`. Las apps que crea el template
> resuelven sus secretos sin tocar nada. El vault legacy sigue en pie para las cuatro apps
> que todavía están fuera del Bicep.

### INTE: las apps que nacen con los roles a mano

`inte.bicepparam` va con **`deployRoleAssignments = false`** por el mismo motivo, con el
SP `b391d418-…` (`sc-chacomer-eip-inte`, objectId `e57cb312-…`), que tiene Contributor
sobre `DataverseINTE` pero no `roleAssignments/write`. Como en INTE
`deployFunctionApps = false`, las apps en juego son las tres que el template sí crea:
**`thinkchat`**, **`ticketatencion`** y **`fiscal`**.

La app nace sin roles, y **sin ellos no arranca**: `AzureWebJobsStorage` va por identidad.
Después de cada deploy que la (re)cree, correr:

```bash
RG=DataverseINTE
APP=fa-axxonthinkchat-inte          # o fa-axxonticketatencion-inte / fa-axxonfiscal-inte / fa-axxoncustomerdata-inte / fa-axxoncustomercredit-inte
PREFIJO=stthinkchatinte             # o stticket / stfiscalinte / stcustdatainte / stcustcredit
MI=$(az functionapp show -g $RG -n $APP --query identity.principalId -o tsv)
ST=$(az storage account list -g $RG --query "[?starts_with(name,'$PREFIJO')].id | [0]" -o tsv)
KV=$(az keyvault show -n kv-chacomer-eip-inte --query id -o tsv)

az role assignment create --assignee-object-id $MI --assignee-principal-type ServicePrincipal --role "Storage Blob Data Owner"          --scope $ST
az role assignment create --assignee-object-id $MI --assignee-principal-type ServicePrincipal --role "Storage Queue Data Contributor"   --scope $ST
az role assignment create --assignee-object-id $MI --assignee-principal-type ServicePrincipal --role "Key Vault Secrets User"           --scope $KV
```

> **Si `az role assignment create` falla con `MissingSubscription`**, no es un problema de
> permisos ni de scope: es un bug del CLI (visto en 2.84.0). Falla incluso pasando
> `--subscription` explícito y con un scope válido. El PUT directo a ARM con los mismos
> datos entra sin chistar:
>
> ```bash
> SUB=09592883-de3a-4c93-944c-222b3c88e832
> ROL=4633458b-17de-408a-b874-0445c86b69e6   # Key Vault Secrets User
> NAME=$(python -c "import uuid;print(uuid.uuid4())")   # el nombre del assignment es un GUID cualquiera
> az rest --method put \
>   --url "https://management.azure.com${KV}/providers/Microsoft.Authorization/roleAssignments/${NAME}?api-version=2022-04-01" \
>   --headers "Content-Type=application/json" \
>   --body "{\"properties\":{\"roleDefinitionId\":\"/subscriptions/${SUB}/providers/Microsoft.Authorization/roleDefinitions/${ROL}\",\"principalId\":\"${MI}\",\"principalType\":\"ServicePrincipal\"}}"
> ```
>
> Los GUID de los tres roles salen de `var roleIds` en
> [`functionApp.bicep`](../../../infra/modules/functionApp.bicep), que es la fuente de
> verdad. Repetir el PUT devuelve `RoleAssignmentExists` y no rompe nada.

Para verificar que quedaron los tres:

```bash
az rest --method get \
  --url "https://management.azure.com/subscriptions/09592883-de3a-4c93-944c-222b3c88e832/resourceGroups/$RG/providers/Microsoft.Authorization/roleAssignments?api-version=2022-04-01&\$filter=principalId eq '$MI'" \
  --query "length(value)" -o tsv
```

Ninguno de esos tres roles cae en la condición ABAC que restringe a `sebastian.diaz@`
(sólo le niega `Owner`, `User Access Administrator` y `Role Based Access Control
Administrator`), así que este paso no depende de nadie más.

Falta además, del lado de Dataverse: la MI de la app tiene que estar dada de alta como
**Application User en Dataverse INTE**. Para thinkchat, con permisos sobre
`axx_metatemplates`; para ticketatencion, los de
[su página](../integraciones/ticketatencion.md#el-application-user-en-dataverse); para
fiscal, lectura sobre `contact` y `account`. Estas apps
van sin `dataverseAuthSettings` a propósito (mismo criterio que `products`): hablan con
Dataverse por managed identity, así que sin ese alta levantan pero fallan al primer llamado.

> **El PPAC pide el Application ID, no el object id.** Son dos GUID distintos de la misma
> managed identity, y es el error clásico. Cómo obtener cada uno:
> [Ticket de Atención › Los dos GUID de la managed identity](../integraciones/ticketatencion.md#los-dos-guid-de-la-managed-identity-y-cuál-va-en-cada-lado).

