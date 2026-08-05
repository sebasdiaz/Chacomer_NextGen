using '../main.bicep'

// Ambiente productivo.
// TODO: URLs sin verificar — el environment todavia no existe en el tenant
// (no aparece en `pac env list`).
param environmentName = 'prod'
param dataverseUrl = 'https://chacomer.crm.dynamics.com'
param foBaseUrl = 'https://chacomer.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'
