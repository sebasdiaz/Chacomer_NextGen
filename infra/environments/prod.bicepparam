using '../main.bicep'

// Ambiente productivo.
param environmentName = 'prod'
param dataverseUrl = 'https://chacomer.crm.dynamics.com'
param foBaseUrl = 'https://chacomer.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'
