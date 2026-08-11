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

// PARCHE: el SP de sc-chacomer-eip-test solo tiene Contributor sobre dataversetest, y
// el PUT de las role assignments pide roleAssignments/write. Las 18 que declara el
// template YA existen y estan completas, asi que saltearlas no cambia nada en runtime.
// Volver a true cuando el SP tenga "Role Based Access Control Administrator" sobre el RG.
param deployRoleAssignments = false
