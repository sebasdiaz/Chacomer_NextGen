// ---------------------------------------------------------------------------
// Service Bus namespace + queues de la EiP.
// Backbone asincronico. Auth por Managed Identity (SAS deshabilitado para
// consumo desde las Functions; el plugin de Dataverse usa una SAS policy
// aparte con permiso Send — se administra fuera de este modulo).
// ---------------------------------------------------------------------------

@description('Sufijo de ambiente (inte, uat, prod).')
param environmentName string

@description('Region de los recursos.')
param location string

@description('Tags comunes.')
param tags object = {}

@description('SKU del namespace. Standard soporta topics y sessions; Premium agrega VNet/private endpoints.')
@allowed([
  'Standard'
  'Premium'
])
param sku string = 'Standard'

@description('''
Queues a crear. Cada item: { name, requiresSession }.
Defaults reflejan lo relevado: contact-master-matching con sessions.
''')
param queues array = [
  {
    name: 'contact-master-matching'
    requiresSession: true
  }
  {
    name: 'account-master-matching'
    requiresSession: true
  }
]

var namespaceName = 'sb-chacomer-eip-${environmentName}'

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource sbQueues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [
  for q in queues: {
    parent: serviceBusNamespace
    name: q.name
    properties: {
      // Sessions: garantizan orden y procesamiento uno-a-uno por SessionId
      // (ej: SessionId = msdyn_identificationnumber para contactos).
      requiresSession: bool(q.requiresSession)
      lockDuration: 'PT5M'
      maxDeliveryCount: 3
      defaultMessageTimeToLive: 'P1D'
      deadLetteringOnMessageExpiration: true
    }
  }
]

output namespaceName string = serviceBusNamespace.name
// FQDN para el app setting ServiceBusConnection__fullyQualifiedNamespace (auth por MI).
output fullyQualifiedNamespace string = '${serviceBusNamespace.name}.servicebus.windows.net'
