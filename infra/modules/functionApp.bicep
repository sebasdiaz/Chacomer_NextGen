// ---------------------------------------------------------------------------
// Function App (Flex Consumption / FC1) de una integracion de la EiP.
// Crea: storage dedicado + plan FC1 + Function App .NET isolated con
// System-Assigned Managed Identity, y las role assignments necesarias:
//   - Storage Blob Data Owner + Storage Queue Data Contributor sobre su storage
//     (AzureWebJobsStorage y deployment container, todo por identidad).
//   - Key Vault Secrets User sobre el vault de la EiP.
//   - Azure Service Bus Data Receiver sobre el namespace (solo si consume SB).
//   - Azure Service Bus Data Sender sobre el namespace (solo si publica en SB).
// ---------------------------------------------------------------------------

@description('Nombre de la Function App. Ej: fa-axxoncontacts-inte')
param functionAppName string

@description('Clave corta para nombrar el storage (<=11 chars). Ej: contacts')
@maxLength(11)
param appKey string

@description('Sufijo de ambiente (inte, uat, prod).')
param environmentName string

@description('Region de los recursos.')
param location string

@description('Tags comunes.')
param tags object = {}

@description('Runtime del worker. Las Functions de la EiP son .NET isolated.')
param runtimeName string = 'dotnet-isolated'

@description('Version del runtime .NET isolated (8.0, 9.0, 10.0). Los proyectos targetean net10.0.')
param runtimeVersion string = '10.0'

@description('Memoria por instancia (MB).')
param instanceMemoryMB int = 2048

@description('Maximo de instancias.')
param maximumInstanceCount int = 40

@description('Connection string de Application Insights (del modulo monitoring).')
param appInsightsConnectionString string

@description('Nombre del Key Vault de la EiP (existente en el mismo RG).')
param keyVaultName string

@description('URI del Key Vault, para el app setting KeyVaultUri.')
param keyVaultUri string

@description('Nombre del namespace de Service Bus (existente en el mismo RG). Vacio si la app no consume SB.')
param serviceBusNamespaceName string = ''

@description('True si esta app consume Service Bus (agrega el role assignment Data Receiver).')
param needsServiceBus bool = false

@description('True si esta app publica en Service Bus (agrega el role assignment Data Sender).')
param publishesToServiceBus bool = false

@description('''
False para NO declarar las role assignments de la MI. El deployment las hace PUT en cada
corrida aunque no cambien, y ese PUT pide `Microsoft.Authorization/roleAssignments/write`:
si el SP del pipeline solo tiene Contributor, todo el deployment falla con
`InvalidTemplateDeployment / Authorization failed`. Ver `deployRoleAssignments` en
main.bicep antes de tocar esto.
''')
param deployRoleAssignments bool = true

@description('App settings propios de la integracion (array de { name, value }).')
param appSettings array = []

// Roles built-in (GUIDs fijos de Azure).
var roleIds = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
}

var deploymentContainerName = 'deploymentpackage'
// Storage global-unique, <=24 chars, solo minusculas/numeros.
var storageAccountName = take('st${appKey}${environmentName}${uniqueString(resourceGroup().id, functionAppName)}', 24)

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    // Sin claves compartidas: todo acceso via Managed Identity.
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${functionAppName}'
  location: location
  tags: tags
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  kind: 'functionapp'
  properties: {
    reserved: true
  }
}

// App settings base (comunes a toda la EiP) + los propios de la integracion.
var baseAppSettings = [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
  {
    name: 'KeyVaultUri'
    value: keyVaultUri
  }
  // AzureWebJobsStorage por identidad (System-Assigned MI), sin connection string.
  {
    name: 'AzureWebJobsStorage__blobServiceUri'
    value: 'https://${storage.name}.blob.${environment().suffixes.storage}'
  }
  {
    name: 'AzureWebJobsStorage__queueServiceUri'
    value: 'https://${storage.name}.queue.${environment().suffixes.storage}'
  }
  {
    name: 'AzureWebJobsStorage__tableServiceUri'
    value: 'https://${storage.name}.table.${environment().suffixes.storage}'
  }
]

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: concat(baseAppSettings, appSettings)
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            // Deployment package leido con la System-Assigned MI de la app.
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
      }
      runtime: {
        name: runtimeName
        version: runtimeVersion
      }
    }
  }
}

// ---- Role assignments (least privilege) ----

resource storageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  name: guid(storage.id, functionApp.id, roleIds.storageBlobDataOwner)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageBlobDataOwner)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  name: guid(storage.id, functionApp.id, roleIds.storageQueueDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.storageQueueDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployRoleAssignments) {
  name: guid(keyVault.id, functionApp.id, roleIds.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.keyVaultSecretsUser)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = if (needsServiceBus || publishesToServiceBus) {
  name: serviceBusNamespaceName
}

resource serviceBusReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (needsServiceBus && deployRoleAssignments) {
  name: guid(resourceGroup().id, serviceBusNamespaceName, functionApp.id, roleIds.serviceBusDataReceiver)
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataReceiver)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Send va separado de Receive a proposito: una app que solo publica no tiene por que
// poder leer las colas de las demas.
resource serviceBusSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (publishesToServiceBus && deployRoleAssignments) {
  name: guid(resourceGroup().id, serviceBusNamespaceName, functionApp.id, roleIds.serviceBusDataSender)
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataSender)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppName string = functionApp.name
output principalId string = functionApp.identity.principalId
output defaultHostName string = functionApp.properties.defaultHostName
