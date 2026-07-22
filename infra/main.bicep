// ===========================================================================
// Enterprise Integration Platform (EiP) — infraestructura base por ambiente.
// Scope: resourceGroup (ej: EiP_Inte).
//
// Despliega lo CROSS a todas las integraciones:
//   - Monitoring: Log Analytics + Application Insights (compartido)
//   - Key Vault (secretos de la plataforma)
//   - Service Bus (backbone asincronico)
//   - Las 4 Function Apps actuales (Flex Consumption + MI + role assignments)
//
// Deploy:
//   az deployment group create -g EiP_Inte \
//     -f infra/main.bicep -p infra/environments/inte.bicepparam
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

module contacts 'modules/functionApp.bicep' = {
  name: 'fa-contacts'
  params: {
    functionAppName: 'fa-axxoncontacts-${environmentName}'
    appKey: 'contacts'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    needsServiceBus: true
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
      { name: 'ServiceBusQueueName', value: 'contact-master-matching' }
      { name: 'AccountServiceBusQueueName', value: 'account-master-matching' }
      // SetApiKey NO va aca: se resuelve desde Key Vault (secret "SetApiKey").
    ]
  }
}

module customers 'modules/functionApp.bicep' = {
  name: 'fa-customers'
  params: {
    functionAppName: 'fa-axxoncustomers-${environmentName}'
    appKey: 'customers'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    needsServiceBus: true
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: serviceBus.outputs.fullyQualifiedNamespace }
    ]
  }
}

module customerGroups 'modules/functionApp.bicep' = {
  name: 'fa-customergroups'
  params: {
    functionAppName: 'fa-axxoncustomergroups-${environmentName}'
    appKey: 'custgroups'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    needsServiceBus: false
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'DualWriteLegalEntities', value: dualWriteLegalEntities }
    ]
  }
}

module products 'modules/functionApp.bicep' = {
  name: 'fa-products'
  params: {
    functionAppName: 'fa-axxonproducts-${environmentName}'
    appKey: 'products'
    environmentName: environmentName
    location: location
    tags: tags
    runtimeVersion: dotnetIsolatedVersion
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    needsServiceBus: false
    appSettings: [
      { name: 'DataverseUrl', value: dataverseUrl }
      { name: 'FoBaseUrl', value: foBaseUrl }
      { name: 'AssignOwningBusinessUnit', value: 'false' }
    ]
  }
}

output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output functionApps array = [
  contacts.outputs.functionAppName
  customers.outputs.functionAppName
  customerGroups.outputs.functionAppName
  products.outputs.functionAppName
]
