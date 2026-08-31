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

// TicketAtencion (GAP-103/227). Se estrena en TEST despues de que el circuito cerrara en
// INTE: la query de la Cita, la function key en el web resource y —la ultima— la subida a
// la biblioteca de la tabla en vez de a la de por defecto.
//
// Contra Dataverse autentica con el app registration compartido, como el resto de TEST
// (`dataverseClientId` arriba). Contra Graph tambien: `graphClientId` no se declara porque
// su default sigue a `dataverseClientId`, y ese registration ya tiene consentidos
// Sites.ReadWrite.All y Files.ReadWrite.All —son tenant-wide, asi que valen igual aca—.
//
// `graphClientSecretName` SI hace falta: kv-chacomer-eip-test tiene el secreto del
// registration bajo `DataverseClientSecret`, no bajo el nombre canonico `GraphClientSecret`.
// Sin la indireccion, EipSecretResolver no lo encuentra, UseClientSecretAuth queda en false
// y la app cae EN SILENCIO a su managed identity — que no esta dada de alta en ningun lado.
param deployTicketAtencionApp = true
param sharePointSiteUrl = 'https://chacomercompy.sharepoint.com/sites/B1-Chacomer-TEST'
param graphClientSecretName = 'DataverseClientSecret'

// Equipo dueño de los masters ("cliente unico"): la BU CLIENTE UNICO existe en TEST
// (businessunitid 6d07f3e2-49a5-f111-b8de-3833c5e62ee5) y su default team se llama igual
// (6e07f3e2-49a5-f111-b8de-3833c5e62ee5), verificado 2026-08-31. Si el equipo se renombra
// o se borra, contacts deja de crear masters y los mensajes caen al DLQ.
param masterOwnerTeamName = 'CLIENTE UNICO'
