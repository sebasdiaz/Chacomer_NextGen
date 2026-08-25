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
    // SIN sessions, igual que INTE (`contacts` en el namespace dataverseinte).
    // Las alimenta el Service Endpoint OOB de Dataverse, que no setea SessionId, y
    // las consume ContactMasterMatchingFunction con IsSessionsEnabled = false.
    // Con requiresSession = true fallan las dos puntas: el publisher no puede enviar
    // y un receiver sin sessions tampoco puede leer.
    name: 'contact-master-matching'
    requiresSession: false
  }
  {
    // Idem, espejo de la cola `accounts` de INTE.
    name: 'account-master-matching'
    requiresSession: false
  }
  {
    // Accounts y contacts de legal entities que Dual Write no sincroniza: van a F&O
    // por API con el mismo mapeo. Sessions por id de registro, para que dos
    // modificaciones del mismo cliente no se procesen fuera de orden.
    name: 'customer-fo-sync'
    requiresSession: true
  }
  {
    // Contraparte de localizacion PY del cliente (LTMCustTable). Cola propia y no un
    // paso mas de customer-fo-sync por dos motivos: un codigo de localizacion invalido
    // no tiene que re-martillar el alta del customer, y las modificaciones de las legal
    // entities que SI estan en Dual Write no pasan por customer-fo-sync (las hace Dual
    // Write), con lo cual la fila quedaria congelada en el alta. Ver ADR-001.
    // Sessions por id de registro, igual que customer-fo-sync.
    name: 'customer-ltm-sync'
    requiresSession: true
  }
  {
    // RemoteExecutionContext del mensaje QualifyLead, publicado por un Service
    // Endpoint de Dataverse. SIN sessions: el Service Endpoint no setea SessionId
    // y con requiresSession la publicacion falla.
    //
    // En INTE esta cola quedo en el namespace viejo (`dataverseinte`, con SAS).
    // Aca se declara dentro del namespace de la EiP porque el trigger comparte
    // `ServiceBusConnection` con customer-fo-sync: las dos tienen que vivir en el
    // mismo namespace. La de INTE se migra en el cutover, no la toca este template.
    name: 'leadcontacts'
    requiresSession: false
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
