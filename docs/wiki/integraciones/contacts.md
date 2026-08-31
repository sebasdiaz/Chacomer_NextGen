<!-- wiki-meta
sources:
  - src/integrations/contacts/**
  - tests/AxxonContacts.Functions.Tests/**
  - pipelines/azure-pipelines-contacts.yml
last_reviewed: 2026-08-31
-->

# Contacts — Master Contact / Golden Record

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

La misma Function App corre **dos** master matchings, uno por entidad:

| Function | Cola | Sessions |
|---|---|---|
| `ContactMasterMatchingFunction` | `%ServiceBusQueueName%` → `contact-master-matching` | no |
| `AccountMasterMatchingFunction` | `%AccountServiceBusQueueName%` → `account-master-matching` | no |

Después del matching, cuando el raw cae en una legal entity **fuera de Dual Write**, esta
app publica un envelope EiP en `customer-fo-sync` que consume
[Customers](customers.md). Ese es el único punto de contacto entre las dos integraciones.

> Las dos colas las alimenta el **Service Endpoint OOB de Dataverse** con su
> `RemoteExecutionContext` nativo, no el plugin. Ver
> [Mensajería › Estado actual vs objetivo](../arquitectura/mensajeria.md#estado-actual-vs-objetivo)
> y la nota sobre `ContactEventPublisherPlugin` en
> [Infraestructura › Queues del namespace](../plataforma/infraestructura.md#queues-del-namespace).

## Setup inicial

### 1. Strong Name Key (plugin — una sola vez)

```powershell
.\generate-snk.ps1
```

> **Lo que sigue de esta sección es el alta manual original.** Hoy los recursos de Azure
> (namespace, colas, Function App, Key Vault, App Insights) los crea el Bicep — ver
> [Infraestructura](../plataforma/infraestructura.md). Queda documentado porque describe
> los requisitos mínimos del flujo y porque las apps de INTE siguen fuera del template.

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
| `MasterOwnerTeamName` | `CLIENTE UNICO` — ver [Owner del master](#owner-del-master-la-business-unit-cliente-unico) |

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

## Owner del master: la business unit CLIENTE UNICO

Los masters —el "cliente único"— se crean asignados al **owner team** que nombra el app
setting `MasterOwnerTeamName`. La business unit de un registro es la del equipo que lo
posee, así que alcanza con el equipo para que todos los masters queden en la misma BU y
la visibilidad se gobierne desde ahí, sin tocar el código cuando cambien los roles.

**Va el nombre del equipo, no su id**, porque el GUID es distinto en cada environment y el
nombre es el mismo. El *default team* de una business unit se llama igual que la BU, así
que `CLIENTE UNICO` resuelve el equipo de esa BU sin tener que crear uno aparte:

| Ambiente | Business unit | Default team (owner) |
|---|---|---|
| INTE | `fe7fa970-48a5-f111-b8de-7c1e525b9d22` | `ff7fa970-48a5-f111-b8de-7c1e525b9d22` |
| TEST | `6d07f3e2-49a5-f111-b8de-3833c5e62ee5` | `6e07f3e2-49a5-f111-b8de-3833c5e62ee5` |

El equipo se resuelve **una vez por instancia** (`MasterOwnerTeamCache`, sin TTL: cambiar
el app setting recicla la app) y sólo pesa al crear un master, no en cada mensaje. Aplica a
las dos entidades: contact master y account master.

**Sin el setting no se asigna owner** y el master queda del usuario con el que corre la app
— el comportamiento anterior. Es lo que pasa en un ambiente donde la BU todavía no existe.

**Con el setting puesto y el equipo ausente, el master no se crea**: se lanza, el mensaje
reintenta y cae al DLQ. Es a propósito. Un master creado en la business unit equivocada no
falla en ningún lado, queda visible para quien no corresponde, y hay que reasignarlo a mano
después; el DLQ, en cambio, se ve. El renombrar o borrar el equipo tiene esa consecuencia.

> Sólo aplica a los masters **nuevos**. Los que ya existían quedan con su owner original:
> moverlos es una reasignación masiva aparte, que este cambio no hace.

En el Bicep es el parámetro `masterOwnerTeamName` (vacío por default), que sólo emite el
app setting si tiene valor. En INTE el template todavía no administra
`fa-axxoncontacts-inte` (`deployFunctionApps = false`), así que ahí el setting va **a mano**
en el portal hasta el cutover — ver [Ambientes](../plataforma/ambientes.md#cutover-de-inte).

## Comportamiento end-to-end

| Escenario | Comportamiento |
|---|---|
| Create/Update de Contact Raw | Plugin publica JSON a Service Bus (SessionId = identification) |
| Contact es Master (`axx_ismaster = true`) | Plugin early exit — no publica nada |
| `msdyn_identificationnumber` vacío | Plugin early exit — no publica nada |
| Dos Raws del mismo cliente al mismo tiempo | Van a la misma Session — se procesan uno a la vez |
| No existe Master | Function crea Master (owner = equipo de `MasterOwnerTeamName`) + BulkAssociate de todos los Raws con misma identification |
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
