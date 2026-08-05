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
└── environments/
    ├── inte.bicepparam
    ├── test.bicepparam
    ├── uat.bicepparam
    └── prod.bicepparam
```

## Ambientes

Un despliegue por ambiente, cada uno sobre su resource group. Todos en la
suscripción `AZURE_DYNAMICS` (`09592883-…`), tenant `d0e6feed-…`, región `eastus`.

| Ambiente | Resource group | Dataverse | F&O | Service connection |
|---|---|---|---|---|
| `inte` | `EiP_Inte` | `operations-b1-chacomer-inte` | `b1-chacomer-inte.sandbox` | `sc-chacomer-eip-inte` |
| `test` | `dataversetest` | `operations-b1-chacomer-test` | `b1-chacomer-test.sandbox` | `sc-chacomer-eip-test` |
| `uat` | *(sin crear)* | — | — | — |
| `prod` | *(sin crear)* | — | — | — |

`dataversetest` ya contenía recursos legacy hechos a mano (`appinsightstest`,
`keyvaultchacomertest`). No colisionan con los nombres del Bicep
(`appi-eip-test`, `kv-chacomer-eip-test`) y quedan fuera de este template.

## Qué despliega

| Recurso | Nombre | Notas |
|---|---|---|
| Log Analytics | `log-eip-{env}` | workspace compartido |
| Application Insights | `appi-eip-{env}` | workspace-based; apps distinguidas por `cloud_RoleName` |
| Key Vault | `kv-chacomer-eip-{env}` | RBAC, purge protection |
| Service Bus | `sb-chacomer-eip-{env}` | Standard; queues con sessions |
| Function Apps | `fa-axxon{dominio}-{env}` | Flex Consumption (FC1), .NET isolated, System-Assigned MI |
| Storage (x app) | `st{app}{env}{hash}` | `allowSharedKeyAccess=false` — solo MI |

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
(`AddEipKeyVault` monta el vault como configuration provider). Ver README raíz,
sección "Key Vault".

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
