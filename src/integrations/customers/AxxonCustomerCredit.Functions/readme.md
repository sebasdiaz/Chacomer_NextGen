# AxxonCustomerCredit.Functions

Expone por HTTP, **en solo lectura**, las cuatro entidades de crédito de clientes de F&O
(`DevAxCustCredit*`) para que las consuman aplicaciones satélite.

El porqué de cada decisión está en
[`docs/wiki/integraciones/customercredit.md`](../../../../docs/wiki/integraciones/customercredit.md).
Acá va sólo lo que hace falta para correrla.

## Endpoints

| Function | Ruta | Entidad de F&O |
|---|---|---|
| `Creditos_Clientes` | `GET /api/creditos/clientes` | `DevAxCustCreditCustomers` |
| `Creditos_Planes` | `GET /api/creditos/planes` | `DevAxCustCreditGrantedPlans` |
| `Creditos_Cuotas` | `GET /api/creditos/cuotas` | `DevAxCustCreditInstallments` |
| `Creditos_Resoluciones` | `GET /api/creditos/resoluciones` | `DevAxCustCreditResolutions` |

Filtros por endpoint (todos opcionales; `top` lo aceptan los cuatro):

| Endpoint | Filtros |
|---|---|
| clientes | `dataAreaId`, `cuenta` |
| planes | `dataAreaId`, `cuenta`, `creditId`, `requestId` |
| cuotas | `dataAreaId`, `cuenta`, `creditId` |
| resoluciones | `dataAreaId`, `solicitudId` |

**Un parámetro que el endpoint no soporta devuelve `400`**, no se ignora. El caso concreto
es `cuenta` en resoluciones: esa entidad no tiene `CustomerAccount`.

Las consultas van con **`cross-company=true`**: devuelven todas las legal entities. Para
una sola, filtrar con `dataAreaId`.

## Correrla local

`local.settings.json` está en el `.gitignore`. Copiar el ejemplo y quedarse con `FoBaseUrl`:

```bash
cp local.settings.example.json local.settings.json
```

Sin `FoClientId` / `FoClientSecret`, el core usa `DefaultAzureCredential`, que localmente
toma la sesión de `az login`. Alcanza con estar logueado en el tenant de F&O:

```bash
az login --tenant d0e6feed-3ca5-4438-bca3-09cb8ba9814a
```

```bash
func start --port 7099
```

Con `AuthorizationLevel.Function` el host local igual acepta las llamadas sin key.

```bash
curl -s "http://localhost:7099/api/creditos/clientes?top=2"
curl -s "http://localhost:7099/api/creditos/planes?cuenta=302001"
curl -s "http://localhost:7099/api/creditos/cuotas?creditId=CRE-0001&top=50"
curl -s "http://localhost:7099/api/creditos/resoluciones?solicitudId=SOL-0001"
```

## Si no arrancan las funciones

### `Worker runtime cannot be 'None'. Please set a valid runtime.`

**Falta `local.settings.json`.** El host muere de una y no carga ninguna función. Pasa
siempre después de clonar o de cambiar de rama: el archivo está en el `.gitignore`, así que
no viaja en el repo. Lo resuelve el `cp` de arriba.

Es el mismo síntoma si `func start` se corre desde la raíz del repo en vez de desde esta
carpeta: el host no encuentra el `host.json` ni el `local.settings.json` del proyecto.

### `Process reporting unhealthy ... azure.functions.webjobs.storage`

**No es un error de las funciones: son las 4 rutas mapeadas y respondiendo.** Es
`AzureWebJobsStorage = UseDevelopmentStorage=true` apuntando a un **Azurite que no está
corriendo**. Aparece con dos textos según cómo esté el setting:

| Setting | Mensaje |
|---|---|
| `UseDevelopmentStorage=true` sin Azurite | `A timeout occurred while running check` (tarda ~15 s) |
| Sin `AzureWebJobsStorage` | `Unable to create client for AzureWebJobsStorage` (inmediato) |

Para estos endpoints da igual: son HTTP puros, no usan colas ni timers ni estado durable.
Se puede ignorar, o silenciarlo levantando Azurite:

```bash
npm install -g azurite
```

```bash
azurite --silent
```

## Estructura

| Pieza | Rol |
|---|---|
| `Functions/CreditosFunction.cs` | Los cuatro endpoints. Valida la query string y arma la respuesta; nada de lógica de F&O. |
| `Services/FoCreditoService.cs` | Arma el `$filter` de cada entidad y lee con el cliente OData del core. |
| `Models/FoCredito*.cs` | Un DTO por entidad, con los nombres de campo tal cual los expone F&O. |
| `Models/CreditoContracts.cs` | Filtros de entrada, topes y el sobre de la respuesta. |

No hay tests todavía — ver el "Pendiente" de la página de la wiki.
