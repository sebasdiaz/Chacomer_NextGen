using '../main.bicep'

// Ambiente de test (RG: dataversetest, eastus).
// URLs verificadas contra `pac env list` (b1-chacomer-test) y DNS de F&O.
param environmentName = 'test'
param dataverseUrl = 'https://operations-b1-chacomer-test.crm.dynamics.com'
param foBaseUrl = 'https://b1-chacomer-test.sandbox.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'

// PARCHE: el SP de sc-chacomer-eip-test solo tiene Contributor sobre dataversetest, y
// el PUT de las role assignments pide roleAssignments/write. Las 18 que declara el
// template YA existen y estan completas, asi que saltearlas no cambia nada en runtime.
// Volver a true cuando el SP tenga "Role Based Access Control Administrator" sobre el RG.
param deployRoleAssignments = false
