using '../main.bicep'

// Ambiente UAT.
param environmentName = 'uat'
param dataverseUrl = 'https://chacomer-uat.crm.dynamics.com'
param foBaseUrl = 'https://chacomer-uat.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'
