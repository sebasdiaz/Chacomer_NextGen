using '../main.bicep'

// Ambiente de integracion (RG: DataverseINTE, eastus).
param environmentName = 'inte'
param dataverseUrl = 'https://chacomer-inte.crm.dynamics.com'
param foBaseUrl = 'https://chacomer-inte.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'
