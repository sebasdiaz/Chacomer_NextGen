using '../main.bicep'

// Ambiente de test (RG: dataversetest, eastus).
// URLs verificadas contra `pac env list` (b1-chacomer-test) y DNS de F&O.
param environmentName = 'test'
param dataverseUrl = 'https://operations-b1-chacomer-test.crm.dynamics.com'
param foBaseUrl = 'https://b1-chacomer-test.sandbox.operations.dynamics.com'
// Legal entities con cdm_isenabledfordualwrite = true en el Dataverse de TEST.
// EN MINUSCULA: alimenta un filtro OData contra F&O (`dataAreaId ne '...'`), y F&O
// devuelve los dataAreaId en minuscula. En Dataverse el cdm_companycode es mayuscula.
// Antes decia 'cha,cne', que no existe en ninguno de los dos lados: el filtro no
// excluia nada y el sync de customer groups pisaba tambien lo de Dual Write.
param dualWriteLegalEntities = 'chac,caut'
param dotnetIsolatedVersion = '10.0'

// Auth contra Dataverse y F&O por Service Principal (app registration
// NextGen_Dynamics365_Inte). Estaban puestos a mano en contacts, customers y
// customergroups: como el template declara la coleccion completa de appSettings, el
// proximo deployment los borraba y las tres apps caian a Managed Identity sin tener
// alta como Application User. No son secretos (los client secret estan en el Key Vault,
// bajo los nombres canonicos DataverseClientSecret y FoClientSecret).
//
// TEMPORAL: el estado deseado es Managed Identity, igual que products, que ya corre asi.
// A medida que cada MI quede dada de alta en Dataverse y como usuario S2S en F&O, se
// vacian estos params y las apps pasan a MI sin tocar codigo.
param dataverseClientId = '145fd64d-3deb-46eb-9f58-736d1ff46a3e'
// Necesario desde que existe el cliente de Dataverse por Web API: ClientSecretCredential
// pide la authority explicita. Sin esto, una app que use la Web API con client secret no
// arranca. Las que van por el SDK o por Managed Identity lo ignoran.
param dataverseTenantId = 'd0e6feed-3ca5-4438-bca3-09cb8ba9814a'
param foClientId = '145fd64d-3deb-46eb-9f58-736d1ff46a3e'
param foTenantId = 'd0e6feed-3ca5-4438-bca3-09cb8ba9814a'

// PARCHE: el SP de sc-chacomer-eip-test solo tiene Contributor sobre dataversetest, y
// el PUT de las role assignments pide roleAssignments/write. Las 18 que declara el
// template YA existen y estan completas, asi que saltearlas no cambia nada en runtime.
// Volver a true cuando el SP tenga "Role Based Access Control Administrator" sobre el RG.
param deployRoleAssignments = false

// Thinkchat (sync de templates -> axx_metatemplates). Se despliega junto con el resto
// (deployFunctionApps queda en true), pero con deployRoleAssignments = false la app nace
// SIN sus roles: al ser nueva no existen de antes, asi que hay que asignarlos a mano
// (Storage Blob Data Owner + Storage Queue Data Contributor sobre su storage, y Key Vault
// Secrets User sobre el vault) o la app no arranca.
// Misma URL que INTE: es el unico endpoint conocido de Thinkchat. Si llega a haber una
// instancia separada para TEST, cambiar aca. get_template es de solo lectura, asi que
// mientras tanto TEST consulta los templates reales sin efectos secundarios.
param thinkchatBaseUrl = 'https://chacomer.whatsapp.net.py/thinkcomm-x/api/v2/'
param thinkchatFrom = '595215180000'

// TicketAtencion (GAP-103/227). La app vive hoy solo en INTE, creada a mano, y es la que
// consume el web resource. En TEST todavia no existe: se deja apagada hasta que alguien
// decida estrenarla ahi, para que un deployment de infra no la cree de rebote.
param deployTicketAtencionApp = false
param sharePointSiteUrl = 'https://chacomercompy.sharepoint.com/sites/B1-Chacomer-TEST'

// Leads (alta desde satelites: Thinkchat, sitio web, campanas). Se despliega junto con el
// resto (deployFunctionApps queda en true), pero con deployRoleAssignments = false la app
// nace SIN sus roles: al ser nueva no existen de antes, asi que hay que asignarlos a mano
// (Storage Blob Data Owner + Storage Queue Data Contributor sobre su storage, Key Vault
// Secrets User sobre el vault, y Azure Service Bus Data Receiver sobre el namespace) o la
// app no arranca ni lee la cola.
//
// A diferencia del resto de las apps de TEST, esta NO usa el app registration: no recibe
// `dataverseAuthSettings`, asi que autentica con su propia MI. Requiere el alta de esa MI
// como Application User en Dataverse TEST con permiso de CREACION sobre `lead`.
param deployLeadsApp = true

// Mismo caso que en INTE: es el default del template, explicito porque todavia no se
// verifico el logical name real contra el Dataverse de TEST.
param leadIdentificationAttribute = 'msdyn_identificationnumber'

// Vacio: sin columna de id externo, la deduplicacion contra Dataverse queda apagada.
param leadExternalIdAttribute = ''
