// ===========================================================================
// Enterprise Integration Platform (EiP) — infraestructura base por ambiente.
// Scope: resourceGroup. Hoy son dos: DataverseINTE (inte) y dataversetest (test).
//
// Despliega lo CROSS a todas las integraciones:
//   - Monitoring: Log Analytics + Application Insights (compartido)
//   - Key Vault (secretos de la plataforma)
//   - Service Bus (backbone asincronico)
//   - Las 5 Function Apps (Flex Consumption + MI + role assignments), salvo que
//     deployFunctionApps sea false
//
// Deploy:
//   az deployment group create -g dataversetest \
//     -f infra/main.bicep -p infra/environments/test.bicepparam
// ===========================================================================

targetScope = 'resourceGroup'

@description('Sufijo de ambiente (inte, uat, prod).')
param environmentName string

@description('Region de los recursos. Default: la del resource group.')
param location string = resourceGroup().location

@description('URL del environment de Dataverse para este ambiente.')
param dataverseUrl string

@description('URL base de Finance & Operations para este ambiente.')
param foBaseUrl string

@description('Legal entities ya sincronizadas por Dual Write (CustomerGroups). CSV, ej: "cha,cne".')
param dualWriteLegalEntities string = ''

@description('''
Client ID del app registration con el que las apps autentican contra Dataverse.
VACIO (default) = autentican con su propia Managed Identity, que es el estado deseado
y lo que debe usar un ambiente nuevo.

Se declara aca y no a mano en cada app porque este template define la coleccion COMPLETA
de appSettings: cualquier setting puesto por fuera lo borra el proximo deployment. Sin
`DataverseClientId`, `DataverseOptions.UseClientSecretAuth` queda en false y la app cae a
Managed Identity en silencio — si esa MI no esta dada de alta como Application User,
falla recien al primer llamado.

No es un secreto: el client secret vive en Key Vault (secret `DataverseClientSecret`).
''')
param dataverseClientId string = ''

@description('''
Client ID del app registration para la auth S2S contra F&O. Mismo criterio que
`dataverseClientId`: vacio = Managed Identity. El secreto va en Key Vault (`FoClientSecret`).
''')
param foClientId string = ''

@description('Tenant ID de Entra para la auth S2S contra F&O. Solo se emite junto con foClientId.')
param foTenantId string = ''

@description('Version del runtime .NET isolated (8.0, 9.0, 10.0).')
param dotnetIsolatedVersion string = '10.0'

@description('''
Maximo de instancias para las apps que llaman a F&O (contacts, customers,
customergroups, products). maxConcurrentCalls en host.json es POR INSTANCIA:
sin este techo la concurrencia real contra F&O se multiplica por N instancias
y se exceden sus limites de API.
''')
param foBoundMaxInstanceCount int = 1

@description('Maximo de instancias para las apps que no pegan a F&O (fiscal).')
param maxInstanceCount int = 40

@description('''
CRON de los timer triggers (NCRONTAB de 6 campos: {seg} {min} {hora} {dia} {mes} {dia-semana}, en UTC).
Van como app settings `Schedules__*`: el doble guion bajo es lo que el host mapea a la
clave jerarquica `Schedules:*` que piden los bindings `%Schedules:X%`. Sin el setting el
binding no resuelve, la funcion queda "in error" y el host levanta con "No job functions
found" — la app corre pero no ejecuta nada, y no falla en ningun lado visible.
''')
param schedules object = {
  customerGroupSync: '0 0 23 * * *'
  productGroupSync: '0 0 23 * * *'
  releasedProductSync: '0 0 * * * *'
  thinkchatTemplateSync: '0 0 */2 * * *'
}

@description('URL base de la API de Thinkchat (con o sin barra final).')
param thinkchatBaseUrl string = ''

@description('Numero emisor que Thinkchat espera en el body de get_template. Ej: 595215180000.')
param thinkchatFrom string = ''

@description('''
False para desplegar SOLO los recursos compartidos (monitoring, Key Vault,
Service Bus) sin tocar las Function Apps. Necesario en ambientes donde las apps
ya existen creadas a mano: este template declara la coleccion completa de
appSettings, asi que adoptarlas de golpe les borraria los secretos en texto
plano (DataverseClientSecret, FoClientSecret, Schedules*) y las dejaria caidas
hasta completar el cutover a Managed Identity + Key Vault.
''')
param deployFunctionApps bool = true

@description('''
Toggle propio de la app de Thinkchat. Existe porque es la unica app GREENFIELD: no hay
una version creada a mano en ningun ambiente, asi que puede desplegarse por Bicep sin
arrastrar el problema de adopcion que mantiene `deployFunctionApps = false` en INTE.

Default: sigue a `deployFunctionApps`. Ponerlo en true de forma independiente exige que
`deployRoleAssignments` tambien este en true — la app corre con AzureWebJobsStorage por
identidad, asi que sin los roles de Storage no arranca. Ver `deployRoleAssignments`.
''')
param deployThinkchatApp bool = deployFunctionApps

@description('''
False para que el template NO declare las role assignments de las Function Apps
(Storage Blob/Queue, Key Vault Secrets User, Service Bus Data Receiver/Sender).

ARM las hace PUT en cada deployment aunque el contenido no cambie, y ese PUT exige
`Microsoft.Authorization/roleAssignments/write`. Con un SP que solo tiene Contributor,
el deployment entero falla con `InvalidTemplateDeployment / Authorization failed` —
aunque las assignments ya existan y esten correctas. El what-if NO lo detecta: no valida
permisos de escritura.

**Es un parche, no el estado deseado.** Con esto en false el template deja de ser la
fuente de verdad del RBAC: una Function App nueva se crea sin sus roles y hay que
asignarlos a mano. Lo correcto es darle al SP del pipeline el rol
"Role Based Access Control Administrator" sobre el RG y volver a true.
''')
param deployRoleAssignments bool = true

var tags = {
  platform: 'EiP'
  environment: environmentName
  managedBy: 'bicep'
}

// Settings de autenticacion por Service Principal. Se emiten solo si el ambiente los
// declara; sin ellos la app usa su Managed Identity. Los *ClientSecret NO van aca:
// se resuelven desde Key Vault por nombre canonico (ver `Secretos` en infra/README.md).
var dataverseAuthSettings = empty(dataverseClientId) ? [] : [
  { name: 'DataverseClientId', value: dataverseClientId }
]

var foAuthSettings = empty(foClientId)
  ? []
  : concat(
      [ { name: 'FoClientId', value: foClientId } ],
      empty(foTenantId) ? [] : [ { name: 'FoTenantId', value: foTenantId } ]
    )

// ---- Recursos compartidos ----

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

// ---- Function Apps ----
// Cada entrada define una integracion. Las role assignments (Key Vault,
// Storage, Service Bus) las resuelve el modulo functionApp.

module contacts 'modules/functionApp.bicep' = if (deployFunctionApps) {
  name: 'fa-contacts'
  params: {
    functionAppName: 'fa-axxoncontacts-${environmentName}'
    appKey: 'contacts'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    maximumInstanceCount: foBoundMaxInstanceCount
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    deployRoleAssignments: deployRoleAssignments
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    needsServiceBus: true
    // Publica en customer-fo-sync los raws de legal entities fuera de Dual Write.
    publishesToServiceBus: true
    // contacts no habla con F&O directo (publica en Service Bus): solo auth de Dataverse.
    appSettings: concat([
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
      { name: 'ServiceBusQueueName', value: 'contact-master-matching' }
      { name: 'AccountServiceBusQueueName', value: 'account-master-matching' }
      { name: 'FoSyncServiceBusQueueName', value: 'customer-fo-sync' }
      // SetApiKey NO va aca: se resuelve desde Key Vault (secret "SetApiKey").
    ], dataverseAuthSettings)
  }
}

module customers 'modules/functionApp.bicep' = if (deployFunctionApps) {
  name: 'fa-customers'
  params: {
    functionAppName: 'fa-axxoncustomers-${environmentName}'
    appKey: 'customers'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    maximumInstanceCount: foBoundMaxInstanceCount
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    deployRoleAssignments: deployRoleAssignments
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    needsServiceBus: true
    appSettings: concat([
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
      // Cola del Service Endpoint de Dataverse (QualifyLead). Sin este setting el
      // trigger de QualifyLeadCustomerSyncFunction no resuelve y la app no arranca.
      { name: 'ServiceBusQueueName', value: 'leadcontacts' }
      { name: 'FoSyncServiceBusQueueName', value: 'customer-fo-sync' }
      // Valor que QualifyLead escribe en msdyn_sellable del contact antes de sincronizar.
      // Sin sellable = true F&O toma el party como prospecto y el alta del customer falla.
      // Sacar el setting apaga el sellado (el contact sincroniza solo si ya venia sellable).
      { name: 'QualifyLeadSellableValue', value: 'true' }
    ], dataverseAuthSettings, foAuthSettings)
  }
}

module customerGroups 'modules/functionApp.bicep' = if (deployFunctionApps) {
  name: 'fa-customergroups'
  params: {
    functionAppName: 'fa-axxoncustomergroups-${environmentName}'
    appKey: 'custgroups'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    maximumInstanceCount: foBoundMaxInstanceCount
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    deployRoleAssignments: deployRoleAssignments
    needsServiceBus: false
    appSettings: concat([
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'DualWriteLegalEntities', value: dualWriteLegalEntities }
      { name: 'Schedules__CustomerGroupSync', value: schedules.customerGroupSync }
    ], dataverseAuthSettings, foAuthSettings)
  }
}

module products 'modules/functionApp.bicep' = if (deployFunctionApps) {
  name: 'fa-products'
  params: {
    functionAppName: 'fa-axxonproducts-${environmentName}'
    appKey: 'products'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    maximumInstanceCount: foBoundMaxInstanceCount
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    deployRoleAssignments: deployRoleAssignments
    needsServiceBus: false
    // SIN dataverseAuthSettings / foAuthSettings a proposito: en TEST esta app ya corre
    // por Managed Identity (nunca tuvo *ClientId) y agregarselos le cambiaria el modo de
    // autenticacion de rebote. Es la unica de las cuatro que ya esta en el estado deseado.
    // Cuando las otras completen su alta de MI, se les saca el param y esto queda parejo.
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'AssignOwningBusinessUnit', value: 'false' }
      { name: 'Schedules__ProductGroupSync', value: schedules.productGroupSync }
      { name: 'Schedules__ReleasedProductSync', value: schedules.releasedProductSync }
    ]
  }
}

// App de consultas fiscales (SET/DNIT + TURUC): solo endpoints HTTP.
// No consume Service Bus ni Dataverse (proxies HTTP puros); superficie publica
// separada del backbone de mensajeria. SetApiKey se resuelve desde Key Vault
// (secret "SetApiKey", via AddEipCore) — no se pasa como app setting.
module fiscal 'modules/functionApp.bicep' = if (deployFunctionApps) {
  name: 'fa-fiscal'
  params: {
    functionAppName: 'fa-axxonfiscal-${environmentName}'
    appKey: 'fiscal'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    // Proxy HTTP puro: no llama a F&O, escala libre.
    maximumInstanceCount: maxInstanceCount
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    deployRoleAssignments: deployRoleAssignments
    needsServiceBus: false
    appSettings: []
  }
}

// Sync de templates de Thinkchat hacia axx_metatemplates (timer cada 2 horas).
// No consume ni publica en Service Bus, y no habla con F&O: solo Dataverse + la API
// de Thinkchat. El token sale del Key Vault (secret "secretThinkChat"), que la MI lee
// con el rol Key Vault Secrets User que cablea el modulo.
module thinkchat 'modules/functionApp.bicep' = if (deployThinkchatApp) {
  name: 'fa-thinkchat'
  params: {
    functionAppName: 'fa-axxonthinkchat-${environmentName}'
    appKey: 'thinkchat'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    // No pega a F&O: no necesita el techo de instancias de las apps F&O-bound.
    maximumInstanceCount: maxInstanceCount
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    deployRoleAssignments: deployRoleAssignments
    needsServiceBus: false
    // SIN dataverseAuthSettings a proposito, mismo criterio que products: al ser una app
    // nueva nace en el estado deseado (Managed Identity). Requiere dar de alta su MI como
    // Application User en Dataverse antes del primer run.
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'ThinkchatBaseUrl', value: thinkchatBaseUrl }
      { name: 'ThinkchatFrom', value: thinkchatFrom }
      { name: 'Schedules__ThinkchatTemplateSync', value: schedules.thinkchatTemplateSync }
      // El token NO va aca: se resuelve desde Key Vault (secret "secretThinkChat").
    ]
  }
}

output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
// Sin output agregado de functionApps: con deployFunctionApps=false los modulos
// no existen y referenciar sus outputs solo agrega warnings BCP318. Los nombres
// son deterministicos (fa-axxon{dominio}-{env}) y nadie consume ese output.
