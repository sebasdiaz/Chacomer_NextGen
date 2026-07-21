# Infraestructura — Enterprise Integration Platform (EiP)

Infra como código (Bicep) de lo cross a todas las integraciones. Un despliegue
por ambiente sobre su resource group.

## Estructura

```
infra/
├── main.bicep                 # orquestador (scope: resourceGroup)
├── modules/
│   ├── monitoring.bicep       # Log Analytics + Application Insights (compartido)
│   ├── keyvault.bicep         # Key Vault (RBAC)
│   ├── servicebus.bicep       # namespace + queues (sessions)
│   └── functionApp.bicep      # Flex Consumption + MI + role assignments
└── environments/
    ├── inte.bicepparam
    ├── uat.bicepparam
    └── prod.bicepparam
```

## Qué despliega

| Recurso | Nombre | Notas |
|---|---|---|
| Log Analytics | `log-eip-{env}` | workspace compartido |
| Application Insights | `appi-eip-{env}` | workspace-based; apps distinguidas por `cloud_RoleName` |
| Key Vault | `kv-chacomer-eip-{env}` | RBAC, purge protection |
| Service Bus | `sb-chacomer-eip-{env}` | Standard; queues con sessions |
| Function Apps | `fa-axxon{dominio}-{env}` | Flex Consumption (FC1), .NET isolated, System-Assigned MI |
| Storage (x app) | `st{app}{env}{hash}` | `allowSharedKeyAccess=false` — solo MI |

Cada Function App recibe, vía role assignment (least privilege):
- **Storage Blob Data Owner** + **Storage Queue Data Contributor** sobre su storage (AzureWebJobsStorage y deployment package, todo por identidad).
- **Key Vault Secrets User** sobre el vault.
- **Azure Service Bus Data Receiver** sobre el namespace (solo contacts y customers, que consumen SB).

## Deploy

```bash
# Requiere: az login + permisos Contributor y User Access Administrator
# sobre el RG (las role assignments necesitan asignar roles).

az deployment group create \
  --resource-group DataverseINTE \
  --template-file infra/main.bicep \
  --parameters infra/environments/inte.bicepparam
```

Previsualizar cambios sin aplicar:

```bash
az deployment group what-if \
  --resource-group DataverseINTE \
  --template-file infra/main.bicep \
  --parameters infra/environments/inte.bicepparam
```

## Secretos en Key Vault (paso posterior al deploy)

Bicep **no** crea valores de secretos (no se exponen en params ni en el state).
Se cargan una vez con `az keyvault secret set`. En producción, Dataverse y F&O
usan Managed Identity, así que los `*ClientSecret` solo existen en el vault de DESA/INTE.

```bash
VAULT=kv-chacomer-eip-inte

# API Key de la SET Paraguay (contacts) — requerido en todos los ambientes
az keyvault secret set --vault-name $VAULT --name SetApiKey --value "<api-key>"

# Client secrets (solo ambientes sin Managed Identity contra Dataverse/F&O)
az keyvault secret set --vault-name $VAULT --name DataverseClientSecret --value "<secret>"
az keyvault secret set --vault-name $VAULT --name FoClientSecret --value "<secret>"
```

El nombre del secret coincide con la clave de configuración que lee el código
(`AddEipKeyVault` monta el vault como configuration provider). Ver README raíz,
sección "Key Vault".

## Pendiente / fuera de alcance de este deploy

- **SAS policy Send** para el plugin de Dataverse sobre la queue (el plugin corre
  en sandbox de Dataverse, no tiene MI): se administra aparte.
- **APIM**: se suma al conectar el primer satélite externo.
- **Data Factory / DMF**: se suma con el primer flujo batch.
- **VNet / private endpoints**: requiere Service Bus Premium y plan Elastic/networking.
