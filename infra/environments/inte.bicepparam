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
// Nace con Managed Identity: este archivo no declara `dataverseClientId`, asi que el
// template no emite ni `DataverseClientId` ni `GraphClientId` y la app autentica con su
// propia MI contra Dataverse y contra Graph. Requiere DOS altas que el Bicep no puede
// hacer, ambas previas al primer uso:
//   1. La MI como Application User en Dataverse INTE, con rol de seguridad.
//   2. Los app roles de Graph (Sites.ReadWrite.All, Files.ReadWrite.All) asignados a la MI.
//      No hay boton en el portal para managed identities: van por Graph API, y los tiene
//      que otorgar un Global Admin.
//
// Y como `deployRoleAssignments` esta en false, la app tambien nace SIN sus roles de
// Storage y Key Vault. Ese paso no es opcional: AzureWebJobsStorage va por identidad, asi
// que sin los roles de Storage la app ni siquiera arranca. Comandos en infra/README.md.
param deployTicketAtencionApp = true
param sharePointSiteUrl = 'https://chacomercompy.sharepoint.com/sites/B1-Chacomer-INTE'

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
