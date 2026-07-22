using '../main.bicep'

// Ambiente de test (RG: EiP_Test, eastus).
param environmentName = 'test'
param dataverseUrl = 'https://operations-b1-chacomer-test.crm.dynamics.com'
param foBaseUrl = 'https://operations-b1-chacomer-test.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'
