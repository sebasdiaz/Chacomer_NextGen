using '../main.bicep'

// Ambiente de integracion (RG: DataverseINTE, eastus).
// URLs verificadas contra `pac env list` (b1-chacomer-inte) y los app settings
// de las Function Apps vivas.
param environmentName = 'inte'
param dataverseUrl = 'https://operations-b1-chacomer-inte.crm.dynamics.com'
param foBaseUrl = 'https://b1-chacomer-inte.sandbox.operations.dynamics.com'
// Legal entities con cdm_isenabledfordualwrite = true en el Dataverse de INTE — son 7,
// distintas de las 2 de TEST. En minuscula: el valor alimenta un filtro OData contra F&O.
// Antes decia 'cha,cne', que no existe en ninguno de los dos lados.
param dualWriteLegalEntities = 'us01,cons,us51,de51,de01,caut,chac'
param dotnetIsolatedVersion = '10.0'

// DataverseINTE tiene 4 Function Apps vivas creadas a mano, con secretos en app
// settings planos y auth por connection string. Adoptarlas de golpe las dejaria
// caidas. Hasta completar el cutover (ver docs/wiki/plataforma/ambientes.md) el Bicep administra en
// INTE solo los recursos compartidos.
param deployFunctionApps = false

// Thinkchat (sync de templates -> axx_metatemplates). Es la unica app greenfield: no
// existe creada a mano, asi que puede nacer administrada por Bicep sin el problema de
// adopcion que mantiene deployFunctionApps en false.
param deployThinkchatApp = true

// El modulo de thinkchat declara 3 role assignments (Storage Blob Data Owner, Storage
// Queue Data Contributor, Key Vault Secrets User) y el SP del pipeline solo tiene
// Contributor sobre este RG. Contributor NO incluye roleAssignments/write, asi que con
// el flag en true el deployment entero muere con Authorization failed. El rol que lo
// destrabaria —Role Based Access Control Administrator— no lo puede otorgar
// sebastian.diaz@: su User Access Administrator esta restringido por una condicion ABAC
// que niega exactamente ese rol.
//
// Por eso la app nace sin roles y los 3 se asignan a mano a su managed identity despues
// del deploy. Ese paso NO es opcional: AzureWebJobsStorage va por identidad, asi que sin
// los roles de Storage la app ni siquiera arranca. Los comandos estan en la seccion
// "Role assignments" de docs/wiki/plataforma/infraestructura.md.
//
// En INTE el flag no tiene efecto colateral: con deployFunctionApps = false, thinkchat
// es la unica app con role assignments en juego.
param deployRoleAssignments = false
param thinkchatBaseUrl = 'https://chacomer.whatsapp.net.py/thinkcomm-x/api/v2/'
param thinkchatFrom = '595215180000'

// TicketAtencion (GAP-103/227). La app creada a mano se borro, asi que —igual que
// thinkchat— deja de ser deuda del cutover y nace administrada por Bicep. Es el motivo del
// toggle propio: `deployFunctionApps` sigue en false por las otras cuatro apps de INTE,
// que todavia no se pueden adoptar.
//
// Autentica con DOS identidades distintas, una por servicio:
//   - Dataverse, por Managed Identity. Este archivo no declara `dataverseClientId`, asi
//     que el template no emite `DataverseClientId`. La MI ya esta dada de alta como
//     Application User en Dataverse INTE (2026-08-28).
//   - Graph, por el app registration compartido `145fd64d` — es la identidad sobre la que
//     un Global Admin otorgo Sites.ReadWrite.All y Files.ReadWrite.All. La managed
//     identity sigue con cero app roles de Graph, asi que por ahi el PDF da 403.
//
// El costo de esta decision, para tenerlo escrito: `Sites.ReadWrite.All` es tenant-wide,
// y ese registration lo comparten las otras seis apps de la EiP — todas quedan con
// escritura sobre todo SharePoint. El camino que acota el permiso a esta sola app es
// mover los dos app roles a su managed identity y volver `graphClientId` a vacio.
//
// El secreto del registration va en Key Vault con el nombre canonico `GraphClientSecret`.
// Bicep no lo crea: `az keyvault secret set --vault-name kv-chacomer-eip-inte --name
// GraphClientSecret --value "<secret>"`. Sin el, `UseClientSecretAuth` queda en false y la
// app cae en silencio a la MI, que es justamente la que no tiene el permiso.
//
// Y como `deployRoleAssignments` esta en false, la app tambien nace SIN sus roles de
// Storage y Key Vault. Ese paso no es opcional: AzureWebJobsStorage va por identidad, asi
// que sin los roles de Storage la app ni siquiera arranca. Comandos en infra/README.md.
param deployTicketAtencionApp = true
param sharePointSiteUrl = 'https://chacomercompy.sharepoint.com/sites/B1-Chacomer-INTE'
param graphClientId = '145fd64d-3deb-46eb-9f58-736d1ff46a3e'
param graphTenantId = 'd0e6feed-3ca5-4438-bca3-09cb8ba9814a'

// Fiscal (consultas SET/DNIT + TURUC + partes por RUC contra Dataverse). Es greenfield
// como thinkchat: `fa-axxonfiscal-inte` no existe creada a mano, asi que no arrastra el
// problema de adopcion que mantiene `deployFunctionApps` en false por las otras apps.
//
// Nace con Managed Identity: este archivo no declara `dataverseClientId`, asi que el
// template no emite `DataverseClientId` y la app autentica con su propia MI contra
// Dataverse. Requiere el alta de esa MI como Application User en Dataverse INTE, con
// lectura sobre contact y account — sin eso `Dataverse_ConsultaRuc` responde 502.
// El resto de los endpoints (SET/TURUC) no dependen de Dataverse: SetApiKey ya esta en
// kv-chacomer-eip-inte y se resuelve por Key Vault.
//
// Y como `deployRoleAssignments` esta en false, la app tambien nace SIN sus roles de
// Storage y Key Vault. Ese paso no es opcional: sin los roles de Storage no arranca.
// Comandos en docs/wiki/plataforma/ambientes.md.
param deployFiscalApp = true

// Equipo dueño de los masters ("cliente unico"): la BU CLIENTE UNICO existe en INTE
// (businessunitid fe7fa970-48a5-f111-b8de-7c1e525b9d22) y su default team se llama igual
// (ff7fa970-48a5-f111-b8de-7c1e525b9d22), verificado 2026-08-31. Como aca
// `deployFunctionApps` esta en false, el template NO administra fa-axxoncontacts-inte:
// hasta el cutover el app setting `MasterOwnerTeamName` hay que ponerlo a mano en el
// portal. Este param queda declarado para que el dia que se adopte la app ya este.
param masterOwnerTeamName = 'CLIENTE UNICO'
