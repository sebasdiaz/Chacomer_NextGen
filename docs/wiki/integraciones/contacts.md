<!-- wiki-meta
sources:
  - src/integrations/contacts/**
  - tests/AxxonContacts.Functions.Tests/**
  - pipelines/azure-pipelines-contacts.yml
last_reviewed: 2026-09-03
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

### Campos custom requeridos en `contact` y en `account`

| Campo | Tipo | Default |
|---|---|---|
| `axx_ismaster` | Boolean | false |
| `axx_tipopersoneriajuridica` | OptionSet | — |

### Campos OOB que deben estar presentes

`parentcontactid`, `a365_contacttype`, `firstname`, `middlename`, `lastname`,
`mobilephone`, `emailaddress1`, `msdyn_customergroupid`, `msdyn_identificationnumber`,
`msdyn_partycountry`, `msdyn_salestaxgroup`

## Configuración de Dual Write

En el Table Map de Contact, agregar Filter Expression (Dataverse → F&O):
```
axx_ismaster eq false
```

## Que se copia del raw al cliente unico

El master no se edita a mano: nace con una copia de los datos del raw que lo disparo.
Ademas del nombre y del bloque de domicilio, se copian:

| Campo | Tipo | Nota |
|---|---|---|
| `emailaddress1` | Texto | En contact tambien viaja `emailaddress2` |
| `axx_lugarcomercial` | Lookup | Solo el Id |
| `axx_tipopersoneriajuridica` | OptionSet | Se copia el valor, no la etiqueta |

> **Estos tres no participan del matching, asi que el PreImage del Step no tiene por que
> traerlos.** En un evento Update solo viajan si cambiaron en esa misma operacion; si no,
> el master se crearia sin ellos y **no fallaria nada**. Por eso, justo antes del Create,
> `EnrichSecondaryFieldsFromDataverseAsync` los relee del raw con un unico `Retrieve` —
> que se paga una vez por identificacion, no en cada mensaje. **No hace falta agregarlos
> al Step**: sumarlos a los Filtering Attributes solo generaria mensajes que no cambian
> nada en el master.

> **Se copian al CREAR el master, y solo ahi.** Cuando el master ya existe, la Function
> asocia el raw y no vuelve a tocar sus campos: un mail o una personeria que cambian
> despues en el raw no llegan al cliente unico. La unica escritura posterior que hace
> **esta Function** sobre el master es la de `SetRucValidationService`, con el resultado
> de la validacion del RUC contra la SET.

> **`axx_dnitresponse` y `axx_fiscalstate` tienen dos escritores.** Ademas de
> `SetRucValidationService`, el [`RucValidatorControl`](../webresources.md) los escribe
> desde el formulario cuando alguien aprieta *Validar*. Desde la v1.0.3 los dos leen la
> **misma** fuente —`GET /api/set/consulta-ruc`, la SET— y comparten el mapeo de estados,
> asi que ya no pueden discrepar sobre un mismo RUC; antes el control consultaba TURUC.
> Lo que sigue valiendo es que **gana el ultimo que escribe**: no hay merge ni orden
> garantizado entre el formulario y el path de mensajeria.
>
> Las dos puntas resuelven el endpoint distinto, y conviene no confundirlas: esta Function
> usa `SetApiService` del core con la key del Key Vault, mientras que el control sale por
> HTTP contra [Fiscal](fiscal.md) con la URL de la environment variable
> `axx_FISCAL_CONSULTA_RUC_URL` — ver [Web resources](../webresources.md). Si el control
> deja de escribir, no es esta Function la que hay que mirar.

## Comportamiento end-to-end

| Escenario | Comportamiento |
|---|---|
| Create/Update de Contact Raw | Plugin publica JSON a Service Bus (SessionId = identification) |
| Contact es Master (`axx_ismaster = true`) | Plugin early exit — no publica nada |
| `msdyn_identificationnumber` vacío | Plugin early exit — no publica nada |
| Dos Raws del mismo cliente al mismo tiempo | Van a la misma Session — se procesan uno a la vez |
| No existe Master | Function crea Master + BulkAssociate de todos los Raws con misma identification |
| Existe Master | Function asocia el Raw. **No propaga campos**: el master conserva los datos con los que se creo |
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
