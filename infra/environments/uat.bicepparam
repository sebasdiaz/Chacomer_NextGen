using '../main.bicep'

// Ambiente UAT.
// TODO: URLs sin verificar — el environment todavia no existe en el tenant
// (no aparece en `pac env list`). El patron real es
// operations-b1-chacomer-{env}.crm.dynamics.com / b1-chacomer-{env}.sandbox.operations.dynamics.com.
param environmentName = 'uat'
param dataverseUrl = 'https://chacomer-uat.crm.dynamics.com'
param foBaseUrl = 'https://chacomer-uat.operations.dynamics.com'
param dualWriteLegalEntities = 'cha,cne'
param dotnetIsolatedVersion = '10.0'
