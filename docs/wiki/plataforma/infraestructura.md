<!-- wiki-meta
sources:
  - infra/**
last_reviewed: 2026-08-21
-->

# Infraestructura (Bicep)

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

Sessions, consumidor y propiedades de cada cola:
**[Colas del Service Bus](../_generado/colas.md)** (generado). Lo que no está en el código
es quién las alimenta:

| Queue | Productor |
|---|---|
| `contact-master-matching` | Service Endpoint de Dataverse |
| `account-master-matching` | Service Endpoint de Dataverse |
| `customer-fo-sync` | `AxxonContacts.Functions` |
| `leadcontacts` | Service Endpoint de Dataverse (QualifyLead) |

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
contra SET/TURUC y `thinkchat` es un timer que no toca F&O, así que ambos escalan con
`maxInstanceCount = 40`. Los dos son params de `main.bicep`, overrideables por ambiente.

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

### Diagnostics: `FunctionAppLogs` al workspace

Cada Function App lleva un diagnostic setting `diag-{functionAppName}` que manda la
categoría **`FunctionAppLogs`** a `log-eip-{env}`, el mismo workspace que respalda a
`appi-eip-{env}`. Se controla con el param `logAnalyticsWorkspaceId` del módulo
`functionApp.bicep`: vacío = sin diagnostic setting.

Es un **backstop de observabilidad**, no un duplicado. Las apps exportan por
OpenTelemetry a Application Insights desde el worker (`AddEipCore`), y ese camino puede
quedar mudo sin que nada falle: el 2026-08-20 `appi-eip-test` estuvo recibiendo sólo de
`fa-axxonproducts-test` mientras contacts, customers y customergroups procesaban mensajes
con el `APPLICATIONINSIGHTS_CONNECTION_STRING` correcto. Volvió sola a las 20:50Z junto
con un reinicio del host, sin causa identificada. Los `FunctionAppLogs` los emite el
**host**, no el worker, así que no comparten ese punto de falla.

A diferencia de las role assignments, esto **no** pide
`Microsoft.Authorization/roleAssignments/write` — alcanza con Contributor sobre el RG, así
que queda prendido también en TEST pese a `deployRoleAssignments = false`.

Para consultarlos:

```
FunctionAppLogs | where _ResourceId endswith "fa-axxoncontacts-test" | order by TimeGenerated desc
```

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

## Pendiente / fuera de alcance de este deploy

- **SAS policy Send** para el plugin de Dataverse sobre la queue (el plugin corre
  en sandbox de Dataverse, no tiene MI): se administra aparte, junto con la
  secure config del plugin que apunta a la queue del ambiente.
- **Import de la solución** a Dataverse (plugins, PCF, web resources).
- **APIM**: se suma al conectar el primer satélite externo.
- **Data Factory / DMF**: se suma con el primer flujo batch.
- **VNet / private endpoints**: requiere Service Bus Premium y plan Elastic/networking.
