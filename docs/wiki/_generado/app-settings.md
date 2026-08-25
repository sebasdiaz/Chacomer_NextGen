<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Application Settings por app

> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está
> mal, el que está mal es el código — o el generador. No edites esta página.

Lo que declara `infra/main.bicep`. **El template declara la colección completa**: un
setting puesto a mano en el portal lo borra el próximo deployment.

### Base — las reciben todas las apps

| Setting |
|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` |
| `KeyVaultUri` |
| `AzureWebJobsStorage__blobServiceUri` |
| `AzureWebJobsStorage__queueServiceUri` |
| `AzureWebJobsStorage__tableServiceUri` |

### `fa-axxoncontacts-{env}`

- appKey: `contacts`
- instancias máx.: `foBoundMaxInstanceCount`
- consume Service Bus: si
- publica en Service Bus: si
- se despliega si: `deployFunctionApps`

| Setting | Valor en el template | Default |
|---|---|---|
| `DataverseUrl` | `dataverseUrl` | — |
| `ServiceBusConnection__fullyQualifiedNamespace` | `serviceBus.outputs.fullyQualifiedNamespace` | — |
| `ServiceBusQueueName` | `'contact-master-matching'` | — |
| `AccountServiceBusQueueName` | `'account-master-matching'` | — |
| `FoSyncServiceBusQueueName` | `'customer-fo-sync'` | — |

Suma `dataverseAuthSettings`, que el template emite sólo si el ambiente declara el client id correspondiente.

### `fa-axxoncustomers-{env}`

- appKey: `customers`
- instancias máx.: `foBoundMaxInstanceCount`
- consume Service Bus: si
- publica en Service Bus: si
- se despliega si: `deployFunctionApps`

| Setting | Valor en el template | Default |
|---|---|---|
| `DataverseUrl` | `dataverseUrl` | — |
| `FoBaseUrl` | `foBaseUrl` | — |
| `ServiceBusConnection__fullyQualifiedNamespace` | `serviceBus.outputs.fullyQualifiedNamespace` | — |
| `ServiceBusQueueName` | `'leadcontacts'` | — |
| `FoSyncServiceBusQueueName` | `'customer-fo-sync'` | — |
| `LtmSyncServiceBusQueueName` | `'customer-ltm-sync'` | — |
| `QualifyLeadSellableValue` | `'true'` | — |

Suma `dataverseAuthSettings` y `foAuthSettings`, que el template emite sólo si el ambiente declara el client id correspondiente.

### `fa-axxoncustomergroups-{env}`

- appKey: `custgroups`
- instancias máx.: `foBoundMaxInstanceCount`
- consume Service Bus: no
- publica en Service Bus: no
- se despliega si: `deployFunctionApps`

| Setting | Valor en el template | Default |
|---|---|---|
| `DataverseUrl` | `dataverseUrl` | — |
| `FoBaseUrl` | `foBaseUrl` | — |
| `DualWriteLegalEntities` | `dualWriteLegalEntities` | — |
| `Schedules__CustomerGroupSync` | `schedules.customerGroupSync` | `0 0 23 * * *` |

Suma `dataverseAuthSettings` y `foAuthSettings`, que el template emite sólo si el ambiente declara el client id correspondiente.

### `fa-axxonproducts-{env}`

- appKey: `products`
- instancias máx.: `foBoundMaxInstanceCount`
- consume Service Bus: no
- publica en Service Bus: no
- se despliega si: `deployFunctionApps`

| Setting | Valor en el template | Default |
|---|---|---|
| `DataverseUrl` | `dataverseUrl` | — |
| `FoBaseUrl` | `foBaseUrl` | — |
| `AssignOwningBusinessUnit` | `'false'` | — |
| `Schedules__ProductGroupSync` | `schedules.productGroupSync` | `0 0 23 * * *` |
| `Schedules__ReleasedProductSync` | `schedules.releasedProductSync` | `0 0 * * * *` |

### `fa-axxonfiscal-{env}`

- appKey: `fiscal`
- instancias máx.: `maxInstanceCount`
- consume Service Bus: no
- publica en Service Bus: no
- se despliega si: `deployFunctionApps`

_Sin app settings propios._

### `fa-axxonthinkchat-{env}`

- appKey: `thinkchat`
- instancias máx.: `maxInstanceCount`
- consume Service Bus: no
- publica en Service Bus: no
- se despliega si: `deployThinkchatApp`

| Setting | Valor en el template | Default |
|---|---|---|
| `DataverseUrl` | `dataverseUrl` | — |
| `ThinkchatBaseUrl` | `thinkchatBaseUrl` | — |
| `ThinkchatFrom` | `thinkchatFrom` | — |
| `Schedules__ThinkchatTemplateSync` | `schedules.thinkchatTemplateSync` | `0 0 */2 * * *` |

### `fa-axxonticketatencion-{env}`

- appKey: `ticket`
- instancias máx.: `maxInstanceCount`
- consume Service Bus: no
- publica en Service Bus: no
- se despliega si: `deployTicketAtencionApp`

| Setting | Valor en el template | Default |
|---|---|---|
| `DataverseUrl` | `dataverseUrl` | — |
| `SharePointSiteUrl` | `sharePointSiteUrl` | — |

Suma `dataverseAuthSettings`, que el template emite sólo si el ambiente declara el client id correspondiente.
