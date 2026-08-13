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
// caidas. Hasta completar el cutover (ver infra/README) el Bicep administra en
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
// "Role assignments" de infra/README.md.
//
// En INTE el flag no tiene efecto colateral: con deployFunctionApps = false, thinkchat
// es la unica app con role assignments en juego.
param deployRoleAssignments = false
param thinkchatBaseUrl = 'https://chacomer.whatsapp.net.py/thinkcomm-x/api/v2/'
param thinkchatFrom = '595215180000'
