# AxxonThinkchat.Functions

Azure Function (.NET 10 isolated) con Timer Trigger que sincroniza los **templates de
Thinkchat** hacia la tabla **`axx_metatemplates`** de Dataverse, cada dos horas.

## Flujo

1. `ThinkchatTemplateSyncFunction` corre segun el CRON `Schedules:ThinkchatTemplateSync`
   (valor sugerido `0 0 */2 * * *` — a las 00, 02, 04... en punto).
2. `ThinkchatTemplateService` hace `POST {ThinkchatBaseUrl}{ThinkchatTemplatesPath}`
   (default `get_template`) con la credencial en el header configurado y el body
   `{ "from": "<ThinkchatFrom>" }`.
3. `TemplateSyncService`:
   - **Upsert** con `UpsertRequest` + `KeyAttributes` contra la **alternate key `axx_id`**
     — Dataverse resuelve Create vs Update en el servidor — en batches de
     `ExecuteMultiple` de 200 (`ContinueOnError = true`: un template fallido no corta el sync).
   - **Barrido de desactivacion**: los registros activos cuyo `axx_id` no vino en la
     corrida pasan a `statecode = Inactive`. No se borran.

## Mapeo

| get_template (Thinkchat) | axx_metatemplates (Dataverse) | Nota                         |
|--------------------------|-------------------------------|------------------------------|
| `id`                     | `axx_id`                      | Clave del upsert             |
| `name`                   | `axx_name`                    |                              |
| `category`               | `axx_category`                |                              |
| `status`                 | `axx_status`                  |                              |
| `type`                   | `axx_type`                    |                              |
| `text`                   | `axx_text`                    | Multilinea                   |
| `variables`              | `axx_variables`               | Vienen numeros; se guarda el JSON crudo |

## Dos guardas de seguridad

- **Si la API devuelve cero templates no se desactiva nada.** Un response vacio es
  indistinguible de una falla silenciosa de Thinkchat, y desactivar la tabla entera por
  un hipo de red no es aceptable. Se loguea Warning y se corta ahi.
- **Los registros sin `axx_id` no se tocan.** No son identificables contra Thinkchat
  (pueden ser carga manual), asi que quedan como estan.

## Application Settings

| Setting | Descripcion |
|---|---|
| `Schedules__ThinkchatTemplateSync` | CRON del timer. Sugerido: `0 0 */2 * * *`. **Doble guion bajo**, ver abajo |
| `WEBSITE_TIME_ZONE` | Zona horaria del CRON (ej. `Paraguay Standard Time`) |
| `ThinkchatBaseUrl` | URL base de la API. Hoy: `https://chacomer.whatsapp.net.py/thinkcomm-x/api/v2/` |
| `ThinkchatTemplatesPath` | Path del endpoint. Default `get_template` |
| `ThinkchatFrom` | Numero emisor que va en el body. En INTE: `595215180000` |
| `ThinkchatApiKeyName` | Solo si el secret deja de llamarse `secretThinkChat` |
| `ThinkchatAuthHeader` | Header de auth. Default `Authorization` |
| `ThinkchatAuthScheme` | Esquema del header. Default `Bearer`. Vacio = valor crudo |
| `ThinkchatTimeoutSeconds` | Timeout HTTP. Default 60 |
| `DataverseUrl` | URL del environment de Dataverse |
| `DataverseClientId` / `DataverseClientSecret` | (DESA) app registration; vacio => Managed Identity |
| `KeyVaultUri` | Vault de los secretos. En INTE: `https://keyvaultinte.vault.azure.net/` |

> **El nombre del setting es `Schedules__ThinkchatTemplateSync`, con doble guion bajo.**
> El binding pide `%Schedules:ThinkchatTemplateSync%` y el host mapea `__` a `:` al leer
> las variables de entorno. Escrito de cualquier otra forma el placeholder no resuelve,
> el host no indexa la Function y **la app queda "Running" con el timer que no corre nunca**
> — sin excepcion ni alerta. Para chequearlo sin esperar al CRON:
> `GET /admin/functions/ThinkchatTemplateSyncFunction/status` con la master key.

## Credenciales

El token sale del **secret `secretThinkChat`** del Key Vault de `KeyVaultUri`. La Managed
Identity de la Function App necesita el rol **Key Vault Secrets User** sobre ese vault.

## Prerequisito en Dataverse

La tabla `axx_metatemplates` necesita una **alternate key sobre `axx_id`** para que el
upsert funcione — ya esta creada en INTE. Sin ella, `UpsertRequest` con `KeyAttributes`
falla con *"entity key attributes do not exist"*.

## Pendiente de confirmar contra la collection de Postman

El esquema de auth y la forma del response estan puestos como valores por defecto
razonables, **no verificados contra el servicio real**. Al confirmarlos se ajusta:

- Header de auth (`ThinkchatAuthHeader` / `ThinkchatAuthScheme`) — via App Settings,
  sin tocar codigo.
- Si el body de `get_template` lleva algun campo mas ademas de `from`.
- `ThinkchatTemplate` (nombres de las propiedades JSON) — un `[JsonPropertyName]` por campo.
- `ThinkchatTemplateService.Deserialize` si el array de templates no esta en la raiz ni
  en `data`/`templates`/`result`.
- Paginacion, si `get_template` la tiene: hoy se asume una sola llamada.
