// ---------------------------------------------------------------------------
// Monitoring: Log Analytics workspace + Application Insights (workspace-based).
// Compartido por las 4 Function Apps de la EiP: todas exportan al mismo
// workspace y se distinguen por cloud_RoleName en las queries.
// ---------------------------------------------------------------------------

@description('Sufijo de ambiente (inte, uat, prod).')
param environmentName string

@description('Region de los recursos.')
param location string

@description('Tags comunes.')
param tags object = {}

var workspaceName = 'log-eip-${environmentName}'
var appInsightsName = 'appi-eip-${environmentName}'

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

output workspaceId string = workspace.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
