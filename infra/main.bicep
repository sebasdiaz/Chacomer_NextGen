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
}

@description('''
False para desplegar SOLO los recursos compartidos (monitoring, Key Vault,
Service Bus) sin tocar las Function Apps. Necesario en ambientes donde las apps
ya existen creadas a mano: este template declara la coleccion completa de
appSettings, asi que adoptarlas de golpe les borraria los secretos en texto
plano (DataverseClientSecret, FoClientSecret, Schedules*) y las dejaria caidas
hasta completar el cutover a Managed Identity + Key Vault.
''')
param deployFunctionApps bool = true

var tags = {
  platform: 'EiP'
  environment: environmentName
  managedBy: 'bicep'
}

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
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    needsServiceBus: true
    // Publica en customer-fo-sync los raws de legal entities fuera de Dual Write.
    publishesToServiceBus: true
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
      { name: 'ServiceBusQueueName', value: 'contact-master-matching' }
      { name: 'AccountServiceBusQueueName', value: 'account-master-matching' }
      { name: 'FoSyncServiceBusQueueName', value: 'customer-fo-sync' }
      // SetApiKey NO va aca: se resuelve desde Key Vault (secret "SetApiKey").
    ]
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
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    needsServiceBus: true
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
      // Cola del Service Endpoint de Dataverse (QualifyLead). Sin este setting el
      // trigger de QualifyLeadCustomerSyncFunction no resuelve y la app no arranca.
      { name: 'ServiceBusQueueName', value: 'leadcontacts' }
      { name: 'FoSyncServiceBusQueueName', value: 'customer-fo-sync' }
    ]
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
    needsServiceBus: false
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'DualWriteLegalEntities', value: dualWriteLegalEntities }
      { name: 'Schedules__CustomerGroupSync', value: schedules.customerGroupSync }
    ]
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
    needsServiceBus: false
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
    needsServiceBus: false
    appSettings: []
  }
}

output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
// Sin output agregado de functionApps: con deployFunctionApps=false los modulos
// no existen y referenciar sus outputs solo agrega warnings BCP318. Los nombres
// son deterministicos (fa-axxon{dominio}-{env}) y nadie consume ese output.
