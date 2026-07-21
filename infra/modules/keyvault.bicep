// ---------------------------------------------------------------------------
// Key Vault de la EiP (uno por ambiente).
// RBAC authorization (no access policies). Los valores de los secrets se
// cargan fuera de Bicep (az keyvault secret set) para no exponerlos en el
// state ni en parametros. Bicep solo crea el vault; las role assignments
// "Key Vault Secrets User" a las Function Apps se hacen en functionApp.bicep.
// ---------------------------------------------------------------------------

@description('Sufijo de ambiente (inte, uat, prod).')
param environmentName string

@description('Region de los recursos.')
param location string

@description('Tags comunes.')
param tags object = {}

var keyVaultName = 'kv-chacomer-eip-${environmentName}'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    // RBAC en lugar de access policies: el control de acceso se hace con
    // role assignments (Key Vault Secrets User a las MI de las Functions).
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
