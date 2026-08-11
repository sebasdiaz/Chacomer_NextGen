# Infraestructura — Enterprise Integration Platform (EiP)

Infra como código (Bicep) de lo cross a todas las integraciones. Un despliegue
por ambiente sobre su resource group.

## Estructura

```
infra/
├── main.bicep                 # orquestador (scope: resourceGroup)
├── modules/
│   ├── monitoring.bicep       # Log Analytics + Application Insights (compartido)
│   ├── keyvault.bicep         # Key Vault (RBAC)
│   ├── servicebus.bicep       # namespace + queues (sessions)
│   └── functionApp.bicep      # Flex Consumption + MI + role assignments
├── environments/
│   ├── inte.bicepparam
│   ├── test.bicepparam
│   ├── uat.bicepparam
│   └── prod.bicepparam
└── scripts/
    └── Set-InteKeyVaultAuth.ps1   # cutover de INTE a Key Vault + MI (apps fuera del Bicep)
```

## Ambientes

**Son dos resource groups, y sólo dos.** Ambos en la suscripción
`AZURE_DYNAMICS` (`09592883-…`), tenant `d0e6feed-…`, región `eastus`.

| Ambiente | Resource group | Dataverse | F&O | Service connection | Function Apps |
|---|---|---|---|---|---|
| `inte` | `DataverseINTE` | `operations-b1-chacomer-inte` | `b1-chacomer-inte.sandbox` | `sc-chacomer-eip-inte` | fuera del Bicep (ver cutover) |
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

1. ✅ **Secretos a Key Vault + System-Assigned MI** — `scripts/Set-InteKeyVaultAuth.ps1`.
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
indirección `DataverseClientSecretName` / `FoClientSecretName` (ver README raíz, sección
"Cuando el secret del vault se llama distinto").

El cutover lo aplica un script idempotente — las apps de INTE están fuera del Bicep, así
que su configuración no puede versionarse en el template:

```powershell
# 1. Dry run
./scripts/Set-InteKeyVaultAuth.ps1 -WhatIf

# 2. App por app: MI + rol sobre el vault + los *SecretName, sin borrar nada
./scripts/Set-InteKeyVaultAuth.ps1 -Apps fa-axxoncontacts-inte

# 3. Validada la app, se borran los secretos planos
./scripts/Set-InteKeyVaultAuth.ps1 -Apps fa-axxoncontacts-inte -RemovePlainSecrets
```

> **Antes del paso 2 en `fa-axxoncustomergroup`:** esa app venía con otro app registration
> (`NextGenInte`, `adcf4b4d-…`) y el script la unifica en `NextGen_Dynamics365_Inte`. Hay que
> confirmar primero que ese registration esté dado de alta como Application User en
> Dataverse INTE y como usuario S2S en F&O con permisos sobre customer groups. Si no,
> correrla con `-SkipClientIdUnification`.

Cuando INTE pase a `deployFunctionApps = true`, `functionApp.bicep` cablea el
`keyVaultUri` del vault que crea el propio template: hay que parametrizarlo para que INTE
siga apuntando a `keyvaultinte`, o migrar los secretos a `kv-chacomer-eip-inte`.

## Qué despliega

| Recurso | Nombre | Notas |
|---|---|---|
| Log Analytics | `log-eip-{env}` | workspace compartido |
| Application Insights | `appi-eip-{env}` | workspace-based; apps distinguidas por `cloud_RoleName` |
| Key Vault | `kv-chacomer-eip-{env}` | RBAC, purge protection |
| Service Bus | `sb-chacomer-eip-{env}` | Standard; 4 queues (ver abajo) |
| Function Apps | `fa-axxon{dominio}-{env}` | Flex Consumption (FC1), .NET isolated, System-Assigned MI |
| Storage (x app) | `st{app}{env}{hash}` | `allowSharedKeyAccess=false` — solo MI |

### Queues del namespace

| Queue | Sessions | Productor | Consumidor |
|---|---|---|---|
| `contact-master-matching` | no | Service Endpoint de Dataverse | `AxxonContacts.Functions` |
| `account-master-matching` | no | Service Endpoint de Dataverse | `AxxonContacts.Functions` |
| `customer-fo-sync` | **sí** | `AxxonContacts.Functions` | `AxxonCustomers.Functions` |
| `leadcontacts` | no | Service Endpoint de Dataverse (QualifyLead) | `AxxonCustomers.Functions` |

**Sólo `customer-fo-sync` lleva sessions.** Es la única cuyo publisher las soporta
(`EipServiceBusPublisher` setea `SessionId`) y la única cuyo trigger declara
`IsSessionsEnabled = true`. Las otras tres las alimenta el **Service Endpoint OOB de
Dataverse, que no setea `SessionId`**, y sus triggers declaran `IsSessionsEnabled = false`:
con `requiresSession` fallan las dos puntas — el publisher no puede enviar
(`The SessionId was not set on a message`) y un receiver sin sessions tampoco puede leer.

Esto es espejo de **INTE**, donde las 5 colas del namespace `dataverseinte` (`contacts`,
`accounts`, `leadcontacts`, `custcustomerv3`, `ingest`) son todas `requiresSession = false`.

> `AxxonContacts.Plugins` incluye un `ContactEventPublisherPlugin` que publica con
> `SessionId`, pero **no está registrado en ningún ambiente**: en INTE el único step del
> assembly es `MasterContactDuplicatePreventionPlugin`. No asumir que las colas de master
> matching se alimentan por plugin.

> `requiresSession` es **inmutable**: no se puede cambiar por deployment. Para pasar una
> cola existente de `true` a `false` hay que borrarla y dejar que el template la recree,
> lo que descarta los mensajes que tenga encoladas.

### CRON de los timer triggers

`customergroups` y `products` disparan por `TimerTrigger` con el schedule en un
placeholder (`%Schedules:CustomerGroupSync%`, `%Schedules:ProductGroupSync%`,
`%Schedules:ReleasedProductSync%`). Los valores salen del param `schedules` de
`main.bicep` y se emiten como app settings **`Schedules__*`** — doble guion bajo, que es
lo que el host mapea a la clave jerarquica `Schedules:*`.

**Si el setting falta o está mal escrito, la app arranca igual y no ejecuta nada:**

```
The 'CustomerGroupSyncFunction' function is in error:
  '%Schedules:CustomerGroupSync%' does not resolve to a value.
No job functions found.
```

Queda en `Running`, sin excepciones ni requests fallidos — sólo esos dos traces al
iniciar el host. Chequeo rápido, sin esperar al horario del CRON:
`GET /admin/functions/<Funcion>/status` con la master key devuelve `{}` si indexó bien.

Las apps de **INTE** están fuera del Bicep y tienen los settings escritos sin separador
(`SchedulesCustomerGroupSync`), que **no resuelve**: `fa-axxoncustomergroup` viene fallando
así. Se corrige en el cutover, junto con el resto de los settings extra.

### Scale-out y límites de F&O

`maxConcurrentCalls` de host.json es **por instancia**, así que sin techo de
instancias la concurrencia real contra F&O se multiplica por N. Por eso las apps
que llaman a F&O por mensaje (`contacts`, `customers`, `customergroups`,
`products`) van con `foBoundMaxInstanceCount = 1`; `fiscal` es un proxy HTTP puro
contra SET/TURUC y escala con `maxInstanceCount = 40`. Ambos son params de
`main.bicep`, overrideables por ambiente.

Cada Function App recibe, vía role assignment (least privilege):
- **Storage Blob Data Owner** + **Storage Queue Data Contributor** sobre su storage (AzureWebJobsStorage y deployment package, todo por identidad).
- **Key Vault Secrets User** sobre el vault.
- **Azure Service Bus Data Receiver** sobre el namespace (solo contacts y customers, que consumen SB).

> **TEST va hoy con `deployRoleAssignments = false`.** ARM hace PUT de las role
> assignments en cada deployment aunque no cambien, y ese PUT exige
> `Microsoft.Authorization/roleAssignments/write`. El SP de `sc-chacomer-eip-test`
> (`67ae2e5d-…`) sólo tiene **Contributor** sobre `dataversetest`, así que el deployment
> entero falla con `InvalidTemplateDeployment / Authorization failed` — aunque las 18
> assignments ya existan y estén correctas. **El what-if no lo detecta**: no valida
> permisos de escritura, así que el error aparece recién en el stage de deploy.
>
> Es un parche: con el flag en false el template deja de ser la fuente de verdad del
> RBAC y una app nueva se crea sin sus roles. Para volver a `true`, alguien con Owner o
> UAA sin condición (el UAA de `sebastian.diaz@` está restringido por ABAC y **no**
> puede) tiene que correr:
>
> ```bash
> az role assignment create --assignee-object-id f57b2a77-e6d4-403d-9846-e6d354abccd9 --assignee-principal-type ServicePrincipal --role "Role Based Access Control Administrator" --scope "/subscriptions/09592883-de3a-4c93-944c-222b3c88e832/resourceGroups/dataversetest"
> ```

## Deploy

Normalmente vía pipeline: `pipelines/azure-pipelines-infra.yml` (INTE, se dispara
por cambios en `infra/**`) y `azure-pipelines-infra-test.yml` (TEST, manual con
gate del environment `test-infra`). A mano:

```bash
# Requiere: az login + permisos Contributor y User Access Administrator
# sobre el RG (las role assignments necesitan asignar roles).

az deployment group create \
  --resource-group dataversetest \
  --template-file infra/main.bicep \
  --parameters infra/environments/test.bicepparam
```

Previsualizar cambios sin aplicar:

```bash
az deployment group what-if \
  --resource-group dataversetest \
  --template-file infra/main.bicep \
  --parameters infra/environments/test.bicepparam
```

> **El error #1 al estrenar un ambiente.** Si la service connection solo tiene
> `Contributor`, los 3 módulos compartidos entran pero las 5 Function Apps
> fallan con `Authorization failed … roleAssignments/write` — y el RG queda a
> medias. Hace falta también **User Access Administrator** (o RBAC
> Administrator) sobre el RG. Es exactamente lo que hizo fallar el primer
> deploy de `EiP_Inte` el 2026-07-23.

```bash
# Otorgar ambos roles a la SP de la service connection (requiere Owner sobre el RG)
RG=/subscriptions/09592883-de3a-4c93-944c-222b3c88e832/resourceGroups/dataversetest
az role assignment create --assignee <OBJECT_ID_SP> --role "Contributor" --scope $RG
az role assignment create --assignee <OBJECT_ID_SP> --role "User Access Administrator" --scope $RG
```

## Secretos en Key Vault (paso posterior al deploy)

Bicep **no** crea valores de secretos (no se exponen en params ni en el state).
Se cargan una vez con `az keyvault secret set`. En producción, Dataverse y F&O
usan Managed Identity, así que los `*ClientSecret` solo existen en el vault de DESA/INTE.

```bash
VAULT=kv-chacomer-eip-test

# API Key de la SET Paraguay (contacts + fiscal) — requerido en todos los ambientes
az keyvault secret set --vault-name $VAULT --name SetApiKey --value "<api-key>"

# Client secrets (solo ambientes sin Managed Identity contra Dataverse/F&O)
az keyvault secret set --vault-name $VAULT --name DataverseClientSecret --value "<secret>"
az keyvault secret set --vault-name $VAULT --name FoClientSecret --value "<secret>"
```

El nombre del secret coincide con la clave de configuración que lee el código
(`AddEipKeyVault` monta el vault como configuration provider). Cuando no coincide —el caso
de `keyvaultinte` en INTE— se declara el nombre real en el Application Setting
`{clave}Name`. Ver README raíz, sección "Key Vault".

## Promoción del código a un ambiente nuevo

La infra crea las Function Apps vacías; el código lo pone el pipeline de cada
integración. Los 5 pipelines (`azure-pipelines-{contacts,customers,customergroups,products,fiscal}.yml`)
extienden `templates/functionapp-build-deploy.yml`, que compila **una sola vez** y
promueve el mismo artifact en cadena:

```
Build ──► Deploy_inte (fa-axxon{dominio}-inte) ──► Deploy_test (fa-axxon{dominio}-test)
                                                   └── gate: approval del environment 'test'
```

El binario que llega a TEST es exactamente el que se validó en INTE — no se
recompila. Para dejar una integración fuera de la promoción, pasarle
`deployToTest: false` en su pipeline.

Alta de un ambiente nuevo, en orden:

1. RG creado y con `Contributor` + `User Access Administrator` para la SP de la SC.
2. Service connection `sc-chacomer-eip-{env}` en Azure DevOps.
3. Environments `{env}` y `{env}-infra` en Azure DevOps, con approvals.
4. Correr el pipeline de infra → crea recursos compartidos + las 5 apps vacías.
5. Cargar los secrets del Key Vault (sección anterior).
6. Application User de cada MI en el Dataverse del ambiente + usuario S2S en F&O.
7. Correr los 5 pipelines de integración.

## Pendiente / fuera de alcance de este deploy

- **SAS policy Send** para el plugin de Dataverse sobre la queue (el plugin corre
  en sandbox de Dataverse, no tiene MI): se administra aparte, junto con la
  secure config del plugin que apunta a la queue del ambiente.
- **Import de la solución** a Dataverse (plugins, PCF, web resources).
- **APIM**: se suma al conectar el primer satélite externo.
- **Data Factory / DMF**: se suma con el primer flujo batch.
- **VNet / private endpoints**: requiere Service Bus Premium y plan Elastic/networking.
