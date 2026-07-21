# AxxonContacts — Master Contact con Azure Service Bus

Plugin thin de Dataverse + Azure Function para unificación de Contacts via patrón Master Contact / Golden Record.

## Arquitectura

```
F&O (CustTable)
  ↓ Dual Write
Dataverse Contact Raw
  ↓ Plugin Post-Op Async (ContactEventPublisherPlugin)
Azure Service Bus Queue — contact-master-matching
  Sessions habilitadas | SessionId = msdyn_identificationnumber
  ↓ Azure Function (ContactMasterMatchingFunction)
Dataverse — Create/Update Master Contact + BulkAssociate + PropagateFields
  (via Managed Identity)
```

## Estructura del solution

```
Chacomer_NextGen/
├── Chacomer.sln
├── .gitignore
├── generate-snk.ps1
│
├── docs/
│   └── contracts/                     (contratos de mensajes de la EiP)
│       └── ServiceBusMessage.json
│
└── src/
    ├── core/
    │   └── Axxon.Eip.Core/            (.NET 10 — componentes CROSS de la EiP)
    │       ├── Configuration/         (DataverseOptions, FoODataOptions, Key Vault)
    │       ├── Dataverse/             (DataverseClientFactory + AddEipDataverse)
    │       ├── FinOps/                (FoODataClient generico + AddEipFoOData + retry 429)
    │       └── Hosting/               (AddEipCore: Key Vault + OpenTelemetry + logging)
    │
    ├── integrations/
    │   ├── contacts/
    │   │   ├── AxxonContacts.Plugins/     (.NET 4.6.2 — plugin Dataverse)
    │   │   ├── AxxonContacts.Functions/   (.NET 10 — Azure Function)
    │   │   └── AxxonContacts.WebResources/
    │   ├── customers/
    │   │   ├── AxxonCustomers.Functions/
    │   │   └── AxxonCustomerGroups.Functions/
    │   └── products/
    │       └── AxxonProducts.Functions/
    │
    └── webresources/                  (PCF controls)
        ├── DeviceRegistrationGrid/
        ├── DnitResponseViewer/
        ├── MasterAccountChildrenGrid/
        ├── MasterContactAccountGrid/
        └── MasterContactChildrenGrid/
```

## Axxon.Eip.Core — componentes cross

Toda Function App de la plataforma referencia `Axxon.Eip.Core` y arranca igual:

```csharp
var builder = FunctionsApplication.CreateBuilder(args);

builder.AddEipCore();                                    // Key Vault + OpenTelemetry/App Insights + logging
builder.Services.AddEipDataverse(builder.Configuration); // IOrganizationService via MI o Client Secret
builder.Services.AddEipFoOData(builder.Configuration);   // cliente OData de F&O con retry 429/Retry-After

// ... servicios propios del dominio ...

builder.Build().Run();
```

Qué provee el core:

| Componente | Descripción |
|---|---|
| `AddEipCore()` | Key Vault como fuente de secretos (si `KeyVaultUri` está seteado), OpenTelemetry exportando a App Insights, logging a consola |
| `AddEipDataverse()` | `DataverseClientFactory` — Managed Identity en Azure, Client Secret en DESA/local |
| `AddEipFoOData()` | `IFoODataClient` — cliente genérico de la OData API de F&O: paginación `@odata.nextLink`, `cross-company`, `$filter`/`$select`, POST tipado, y retry SOLO ante HTTP 429 respetando `Retry-After` |

## Key Vault — manejo de secretos (obligatorio)

Todos los secretos viven en Azure Key Vault (un vault por ambiente). Nada de secretos
en Application Settings planos ni en el repo.

### Configuración

1. Crear el Key Vault (ej: `kv-chacomer-eip-{env}`) con **RBAC authorization**.
2. Asignar a la Managed Identity de cada Function App el rol **Key Vault Secrets User**:

```bash
az role assignment create \
  --assignee <PRINCIPAL_ID_DE_LA_MI> \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/.../resourceGroups/.../providers/Microsoft.KeyVault/vaults/kv-chacomer-eip-prod
```

3. Agregar el Application Setting `KeyVaultUri = https://kv-chacomer-eip-{env}.vault.azure.net/`
   en cada Function App. Con eso `AddEipCore()` carga el vault como configuration provider:
   cada secret se expone como clave de configuración con su mismo nombre, y **pisa** cualquier
   App Setting duplicado.

### Convención de nombres de secrets

| Secret en Key Vault | Usado por | Descripción |
|---|---|---|
| `DataverseClientSecret` | todas (solo DESA) | Client Secret del App Registration de Dataverse |
| `FoClientSecret` | customers, products (solo DESA) | Client Secret del App Registration de F&O |
| `SetApiKey` | contacts | API Key de la SET Paraguay |

En producción las conexiones a Dataverse/F&O usan Managed Identity: no hay secreto que guardar.
Los secrets `*ClientSecret` solo existen en el vault de DESA.

### Triggers y bindings (resueltos por el host, no por el worker)

Los settings que consume el **host** de Functions (ej: `ServiceBusConnection` de los triggers,
binding expressions `%...%`) NO pasan por el configuration provider del worker. Para esos:

- **Producción:** Managed Identity — `ServiceBusConnection__fullyQualifiedNamespace` (sin secreto).
- **DESA con connection string:** Key Vault reference en el Application Setting:
  `@Microsoft.KeyVault(SecretUri=https://kv-chacomer-eip-desa.vault.azure.net/secrets/ServiceBusConnection/)`

### Desarrollo local

Dos opciones:
- `az login` + `KeyVaultUri` en `local.settings.json` → lee los secrets del vault de DESA.
- Sin `KeyVaultUri` → los valores se toman de `local.settings.json` como siempre.

## Setup inicial

### 1. Strong Name Key (plugin — una sola vez)

```powershell
.\generate-snk.ps1
```

### 2. Recursos Azure requeridos

Crear en Azure:
- **Service Bus Namespace** (Standard o Premium)
- **Queue** con nombre `contact-master-matching`
  - **Sessions: habilitado** (obligatorio)
  - Lock Duration: 5 min
  - Max Delivery Count: 3
  - TTL: 24 hs
- **SAS Policy** con permiso `Send` sobre la queue (para el plugin)
- **SAS Policy** con permiso `Listen` sobre la queue (para la Function — o usar Managed Identity)
- **Function App** (.NET 8, Azure Functions v4)
- **Application Insights** asociado a la Function App

### 3. Managed Identity (produccion)

```bash
# Habilitar System Assigned Managed Identity en la Function App
az functionapp identity assign --name TU_FUNCTION_APP --resource-group TU_RG

# Asignar rol de Azure Service Bus Data Receiver sobre la queue
az role assignment create \
  --assignee <PRINCIPAL_ID_DE_LA_MI> \
  --role "Azure Service Bus Data Receiver" \
  --scope /subscriptions/.../resourceGroups/.../providers/Microsoft.ServiceBus/namespaces/.../queues/contact-master-matching
```

En Power Platform Admin Center:
- Environments → Tu Env → Settings → Users → App Users → New App User
- Seleccionar la Managed Identity
- Asignar Security Role con permisos sobre la entidad `contact`

### 4. Application Settings de la Function App (produccion)

| Setting | Valor |
|---|---|
| `DataverseUrl` | `https://tuorg.crm.dynamics.com` |
| `ServiceBusQueueName` | `contact-master-matching` |
| `ServiceBusConnection__fullyQualifiedNamespace` | `tunamespace.servicebus.windows.net` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | cadena de conexion de App Insights |
| `KeyVaultUri` | `https://kv-chacomer-eip-{env}.vault.azure.net/` |

> Con Managed Identity NO se configura `ServiceBusConnection` como connection string completa.
> Se usa el formato `__fullyQualifiedNamespace` que activa la auth via MI automáticamente.

### 5. Desarrollo local

Copiar `local.settings.json` y completar:
- `ServiceBusConnection`: connection string completa (con SharedAccessKey)
- `DataverseUrl`: URL del environment de DESA
- `DataverseClientId` / `DataverseClientSecret`: App Registration de DESA

## Registration del Plugin (Plugin Registration Tool)

### Assembly

- **Isolation Mode:** Sandbox
- **Location:** Database

### Step 1 — Create

| Property | Value |
|---|---|
| Plugin | `AxxonContacts.Plugins.ContactEventPublisherPlugin` |
| Message | Create |
| Entity | contact |
| Stage | Post-Operation (40) |
| Mode | Asynchronous |
| Filtering Attributes | (vacío) |
| **Secure Configuration** | `{connectionString}\|{queueName}` |

Ejemplo Secure Config:
```
Endpoint=sb://axxon.servicebus.windows.net/;SharedAccessKeyName=SendOnly;SharedAccessKey=xxxx|contact-master-matching
```

### Step 2 — Update

| Property | Value |
|---|---|
| Plugin | `AxxonContacts.Plugins.ContactEventPublisherPlugin` |
| Message | Update |
| Entity | contact |
| Stage | Post-Operation (40) |
| Mode | Asynchronous |
| **Secure Configuration** | mismo que Step 1 |

**Filtering Attributes:**
```
axx_ismaster, msdyn_identificationnumber, a365_contacttype,
firstname, middlename, lastname, mobilephone, emailaddress1,
msdyn_customergroupid, msdyn_partycountry, msdyn_salestaxgroup
```

**Pre-Image** (alias `PreImage`):
```
parentcontactid, axx_ismaster, msdyn_identificationnumber,
a365_contacttype, firstname, middlename, lastname, mobilephone,
emailaddress1, msdyn_customergroupid, msdyn_partycountry, msdyn_salestaxgroup
```

> CRITICO: NO incluir `parentcontactid` en Filtering Attributes.

## Configuración de Dataverse

### Campo custom requerido en la tabla `contact`

| Campo | Tipo | Default |
|---|---|---|
| `axx_ismaster` | Boolean | false |

### Campos OOB que deben estar presentes

`parentcontactid`, `a365_contacttype`, `firstname`, `middlename`, `lastname`,
`mobilephone`, `emailaddress1`, `msdyn_customergroupid`, `msdyn_identificationnumber`,
`msdyn_partycountry`, `msdyn_salestaxgroup`

## Configuración de Dual Write

En el Table Map de Contact, agregar Filter Expression (Dataverse → F&O):
```
axx_ismaster eq false
```

## Comportamiento end-to-end

| Escenario | Comportamiento |
|---|---|
| Create/Update de Contact Raw | Plugin publica JSON a Service Bus (SessionId = identification) |
| Contact es Master (`axx_ismaster = true`) | Plugin early exit — no publica nada |
| `msdyn_identificationnumber` vacío | Plugin early exit — no publica nada |
| Dos Raws del mismo cliente al mismo tiempo | Van a la misma Session — se procesan uno a la vez |
| No existe Master | Function crea Master + BulkAssociate de todos los Raws con misma identification |
| Existe Master | Function asocia el Raw y propaga solo los campos del ChangedFields al Master |
| Function falla | Service Bus reintenta x3 → DLQ |
| Mensaje no deserializable | Dead Letter inmediato (no reintenta) |

## Build y deploy

```powershell
# Plugin
cd src\integrations\contacts\AxxonContacts.Plugins
dotnet build -c Release

# Function
cd src\integrations\contacts\AxxonContacts.Functions
dotnet build -c Release
dotnet publish -c Release -o ./publish

# Deploy Function App (Azure CLI)
az functionapp deployment source config-zip \
  --name TU_FUNCTION_APP \
  --resource-group TU_RG \
  --src ./publish.zip
```

## Troubleshooting

| Herramienta | Donde |
|---|---|
| Plugin logs | Dataverse → System Jobs → filtrar `ContactEventPublisherPlugin` |
| Function logs | Application Insights → Logs → traces \| where cloud_RoleName == "AxxonContacts.Functions" |
| Mensajes fallados | Service Bus → Queues → contact-master-matching → Dead Letter |
| Metrics | Application Insights → Failures / Performance |

## Out of scope V1

- Re-matching ante cambio de `msdyn_identificationnumber`
- Propagacion cuando el Master ya existe y hay Raws huerfanos (cubrir con backfill)
- ActivityRedirectionPlugin (Plugin 2)
- DLQ handler automatico
- Backfill inicial
