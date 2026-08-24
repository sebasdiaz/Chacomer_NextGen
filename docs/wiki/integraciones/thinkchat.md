<!-- wiki-meta
sources:
  - src/integrations/thinkchat/**
  - pipelines/azure-pipelines-thinkchat.yml
last_reviewed: 2026-08-21
-->

# Thinkchat — WhatsApp/SMS

Function App (.NET 10 isolated) de la integracion con **Thinkchat** (BSP de WhatsApp/SMS).
Tres functions:

| Function | Trigger | Que hace |
|---|---|---|
| `ThinkchatTemplateSyncFunction` | Timer, cada 2 h | Sincroniza los templates hacia `axx_metatemplates` |
| `Thinkchat_SendTemplate` | HTTP `POST /api/thinkchat/send-template` | Envia una plantilla HSM de WhatsApp |
| `Thinkchat_SendText` | HTTP `POST /api/thinkchat/send-text` | Envia texto libre en sesion (ventana de 24 h) |

## Sync de templates — flujo

1. `ThinkchatTemplateSyncFunction` corre segun el CRON `Schedules:ThinkchatTemplateSync`
   (valor sugerido `0 0 */2 * * *` — a las 00, 02, 04... en punto).
2. `ThinkchatTemplateService` hace `POST {ThinkchatBaseUrl}` con `Authorization: Bearer`
   y el body `{ "action": "get_templates", "from": "<ThinkchatFrom>" }`.
   El response es `{ "success": true, "templates": [ ... ] }`.
3. `TemplateSyncService`:
   - **Upsert** con `UpsertRequest` + `KeyAttributes` contra la **alternate key `axx_id`**
     — Dataverse resuelve Create vs Update en el servidor — en batches de
     `ExecuteMultiple` de 200 (`ContinueOnError = true`: un template fallido no corta el sync).
   - **Barrido de desactivacion**: los registros activos cuyo `axx_id` no vino en la
     corrida pasan a `statecode = Inactive`. No se borran.

## La API es RPC, no REST

Thinkchat expone **un solo endpoint** (`{ThinkchatBaseUrl}`) y todas las operaciones
son `POST` contra el, con el verbo logico en el campo `action` del body
(`get_templates`, `send_template`, `send_sms`, `create_lead`…). **No existe una ruta
`/get_templates`**: pedirla devuelve el 404 de nginx, que despista porque parece la
URL base mal armada. Un token invalido, en cambio, da 401 con
`{"success":false,"msg":"Token invalido"}` — util para distinguir un problema de
ruta de uno de credencial.

## Envio de plantillas — `POST /api/thinkchat/send-template`

`AuthorizationLevel.Function`: hace falta la function key (`x-functions-key`).

```json
{
  "to": "595981000000",
  "template_id": "29301236-a1c1-45a4-a15c-cdad9207c2e4",
  "template_params": ["Jorge", "NX 350h"],
  "template_media": "",
  "extras": { "inbound_expiration": 60, "inbound_queue": 1 }
}
```

- **`from` no se recibe**: sale de `ThinkchatFrom`. Dejar que el caller elija la linea
  emisora seria una forma facil de mandar desde la linea equivocada.
- `template_id` es el `axx_id` de `axx_metatemplates`.
- `template_params` es **posicional**: reemplaza los `{{1}}`, `{{2}}`… del texto en orden.
- `template_media` solo si la plantilla tiene header de media.
- `extras` es opcional. `inbound_expiration` va en **minutos**.

### Validacion contra axx_metatemplates

Antes de llamar a la API, la Function hace un `Retrieve` por `axx_id` y rechaza con 400 si:

| Caso | Motivo |
|---|---|
| El `template_id` no esta en la tabla | Puede ser una plantilla nueva que el sync todavia no trajo |
| El registro esta inactivo | Lo desactivo el barrido: ya no viene de Thinkchat |
| `axx_status` no es `APPROVED` | Meta lo rechaza igual, pero del otro lado el error es opaco |
| `template_params.Count != axx_variables` | La cantidad no coincide con lo que espera la plantilla |
| `axx_type` no es `text` y no vino `template_media` | La plantilla tiene header de media y necesita la URL del adjunto |

Las dos ultimas son las que importan: un envio con la cantidad equivocada **llega al
cliente con el texto roto y ya se cobro**. Un `Retrieve` es mas barato que un mensaje
quemado.

`axx_variables` es la fuente de verdad de la cantidad — verificado contra los 112
templates de INTE, donde coincide con el maximo `{{n}}` del texto en todos. Si algun dia
deja de ser un numero, se loguea Warning y **no se bloquea el envio**: es peor frenar
mensajes legitimos por un cambio de formato del proveedor.

La de media salio de un envio real que fallo: `b2b_ofertas_rmjulio` es `type=image` y con
`template_media` vacio la API responde `{"success":false,"msg":"template_media invalido"}`.
**No es un borde: 26 de los 77 APPROVED activos de INTE necesitan media** (22 `image`,
4 `video`). La condicion es por exclusion —cualquier `axx_type` que no sea `text`— porque
Meta soporta mas tipos que los dos que se ven hoy y un type nuevo no deberia pasar de largo.

> El texto (`axx_text`) no hace falta para enviar: la API solo quiere `template_id` y los
> parametros. Se lee igual en el `Retrieve` porque sale gratis y sirve para los logs.

`IMetatemplateLookup` va como **singleton** con el `ServiceClient` creado perezosamente:
`DataverseClientFactory` es transient y cada resolucion arma una conexion nueva con su
handshake de ~1s. Para el timer da igual; para un endpoint HTTP, no.

**Un solo destino de inbound a la vez** (`inbound_bot` / `inbound_queue` /
`inbound_agent`): la Function rechaza con 400 si viene mas de uno. El proveedor lo
advierte en su doc y despues su propio ejemplo manda los cuatro juntos; ante la
contradiccion, mejor fallar de este lado que dejar que la API elija en silencio.
`inbound_interaccion` es un nombre, no un destino, y no cuenta para la regla.

### Respuestas

| Codigo | Significado |
|---|---|
| 200 | La API acepto el envio. Se devuelve su body crudo |
| 400 | El pedido no paso la validacion local. **No se llamo a Thinkchat** |
| 502 | Thinkchat rechazo el envio o fallo. Se devuelve su body crudo, con el `msg` |

El 502 es a proposito y no el status del proveedor: propagar un 401 de Thinkchat le
haria pensar al caller que su propia llamada quedo sin autorizar.

### El contrato real de la respuesta

La doc del proveedor dice que estas operaciones no devuelven body. **Es falso**, relevado
contra la API real:

```json
{
  "success": true,
  "msg": "Sent",
  "date": "2026-08-14 18:07:55",
  "msg_id": "baddb7b9-9867-4480-9a23-132b4838f5e5",
  "ihash": "dd2cc75f2e19d8fd27bd97753a64bde896eaa6611cb23edd6a269d70dadb96b0",
  "api_version": 2
}
```

En el error, el mismo objeto con `success: false` y el motivo en `msg` (ej.
`"template_media invalido"`, `"Token invalido"`).

**Hay `msg_id`**, asi que se puede correlacionar el envio con el mensaje del lado del
proveedor. Eso habilita idempotencia y seguimiento de entrega, que se daban por
imposibles. Falta confirmar con el proveedor si existe un endpoint para consultar el
estado (delivered / read / failed) a partir de ese id — no esta en la collection.

Igual se sigue devolviendo el body crudo: el contrato no esta documentado del lado de
ellos y puede cambiar sin aviso.

## Texto libre en sesion — `POST /api/thinkchat/send-text`

`AuthorizationLevel.Function`: hace falta la function key (`x-functions-key`).

```json
{
  "to": "595981000000",
  "text": "Hola, seguimos con tu consulta."
}
```

- **`from` no se recibe**: sale de `ThinkchatFrom`, igual que en send-template.
- `text` con limite de **4096 caracteres** (el tope de WhatsApp para texto).
- No valida contra `axx_metatemplates`: no hay plantilla que validar.

### La ventana de 24 horas

Este mensaje **solo llega si la conversacion tiene la ventana de 24 h abierta**, y esa
ventana la abre unicamente un mensaje **entrante** del cliente — es una regla de la
plataforma de WhatsApp (Meta), no de Thinkchat, y por eso **no existe un `action` para
abrir sesion** en la API. Cada mensaje del cliente la renueva otras 24 h.

Para iniciar una conversacion desde este lado el flujo es:

1. `POST /api/thinkchat/send-template` — plantilla aprobada por Meta, con `extras`
   ruteando la respuesta (`inbound_queue` / `inbound_agent` / `inbound_bot`).
2. El cliente responde → se abre la ventana.
3. `POST /api/thinkchat/send-text` — texto libre mientras la ventana dure.

Con la ventana cerrada el proveedor rechaza; la Function devuelve 502 con su body
crudo. **El contrato de ese error no esta documentado** — mismo criterio de
relevamiento que send-template: mirar el `msg` de los 502 en App Insights.

## Mapeo del sync

| get_templates (Thinkchat) | axx_metatemplates (Dataverse) | Nota                         |
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
| `ThinkchatBaseUrl` | URL base de la API — **es el endpoint**. Hoy: `https://chacomer.whatsapp.net.py/thinkcomm-x/api/v2/` |
| `ThinkchatTemplatesPath` | Path relativo. **Default vacio** y asi debe quedar: el endpoint es la base |
| `ThinkchatTemplatesAction` | Verbo del body. Default `get_templates` (plural) |
| `ThinkchatSendTemplateAction` | Verbo del body para el envio. Default `send_template` |
| `ThinkchatSendTextAction` | Verbo del body para el texto en sesion. Default `send_text_msg` |
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

## Lo que sigue sin contrato

El endpoint, el auth y la forma del response ya estan verificados contra la collection
de Postman del proveedor. Queda abierto:

- **Paginacion**: la doc no la menciona y hoy se asume una sola llamada. Con decenas de
  templates no molesta; si la cantidad crece, revisar.
- **`date`**: el response lo trae y no se mapea — `axx_metatemplates` no tiene columna.
