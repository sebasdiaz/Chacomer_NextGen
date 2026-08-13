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
//
// Para crearla hay que agregar aca `param deployThinkchatApp = true`. NO se deja puesto
// todavia porque el modulo declara 3 role assignments (Storage Blob, Storage Queue,
// Key Vault Secrets User) y el SP del pipeline sobre este RG no tiene
// roleAssignments/write: el deployment entero fallaria con Authorization failed. Y
// saltearlas con deployRoleAssignments = false no sirve — sin los roles de Storage la
// app ni siquiera arranca (AzureWebJobsStorage va por identidad).
// Ver "Role assignments" en infra/README.md.
param thinkchatBaseUrl = 'https://chacomer.whatsapp.net.py/thinkcomm-x/api/v2/'
param thinkchatFrom = '595215180000'
