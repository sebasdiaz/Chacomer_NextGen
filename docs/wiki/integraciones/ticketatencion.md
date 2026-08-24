<!-- wiki-meta
sources:
  - src/integrations/service/AxxonTicketAtencion.Functions/**
  - src/core/Axxon.Eip.Core/Graph/**
  - src/core/Axxon.Eip.Core/Dataverse/DataverseWebApiClient.cs
  - tests/AxxonTicketAtencion.Functions.Tests/**
  - pipelines/azure-pipelines-ticketatencion.yml
last_reviewed: 2026-08-24
-->

# Ticket de Atención — Orden de Reparación

Azure Function (.NET 10 isolated) que genera el **Ticket de Atención** de una Cita de
Servicio en Word, lo devuelve para que el navegador lo abra, y en paralelo lo convierte a
PDF y lo adjunta a la Cita en SharePoint.

GAP-103 / GAP-227. Reemplaza a `AzureFunctions/TicketAtencion/` del repo
**Chacomer Dataverse**, que queda a deprecar.

Es la única integración disparada por un **usuario** —un asesor de servicio apretando un
botón— y no por un mensaje, un timer o un sistema. De ahí salen sus dos rarezas: es la
única app con CORS, y la única que devuelve un archivo en el body.

## La cadena

```
[D365] Formulario de Cita de Servicio (msauto_serviceappointment)
   │  botón "Generar Ticket Atención"
   ▼
[Web Resource] axx_/ServiceAppointment/form.js     ← repo Chacomer Dataverse
   │  POST { serviceAppointmentId } + x-functions-key
   ▼
[Azure Function] GenerarTicketAtencion
   ├─► Dataverse Web API v9.2      (6 queries)
   ├─► template_ticket_atencion.docx (Custom XML Part)
   └─► Microsoft Graph              (PDF + upload a SharePoint)
   │
   ▼  { status, wordBase64, fileName, url, wordBytes }
[Web Resource] abre el documento
```

## Endpoint

| Function | Ruta | Auth |
|---|---|---|
| `GenerarTicketAtencion` | `POST /api/GenerarTicketAtencion` | Function key |

Body: `{ "serviceAppointmentId": "<guid>" }`.

| Status | Cuándo |
|---|---|
| `200` + `status: "OK"` | Word generado y PDF adjuntado. |
| `200` + `status: "OK_SIN_PDF"` | Word generado; falló el PDF o SharePoint. **`url` viene vacía: el cliente debe usar `wordBase64`.** |
| `400` | Body vacío, no-JSON, sin `serviceAppointmentId` o con un GUID malformado. |
| `401` | Sin function key. Lo resuelve el host. |
| `404` | La Cita no existe. |
| `500` | Falla interna. |

Los errores devuelven `{ "status": "ERROR", "mensaje": "…" }` con un texto apto para el
usuario final. **Ningún response lleva stack traces**: el detalle va a
[Application Insights](../plataforma/telemetria.md).

**El PDF es best-effort y no puede tumbar la respuesta.** Una vez que el Word está armado,
nada de lo que siga cambia el `200`. El asesor obtiene su documento aunque SharePoint esté
caído.

## Por qué la Web API y no el SDK

Es la única integración que habla con Dataverse por **OData** (`AddEipDataverseWebApi`) en
vez del `ServiceClient` del SDK. La consulta principal trae la Cita con `$expand` anidados
de dos niveles —Cita → Dispositivo → Marca/Modelo/Color/CódigoProducto— y en FetchXML eso
queda mucho más oscuro. Las otras cinco queries (empresa, dirección, trabajos, notas y
último kilometraje) salen en paralelo una vez resuelta la principal.

Un error de cualquiera de las seis **aborta**: `DataverseWebApiClient` lanza en vez de
devolver vacío. Un ticket al que le faltan los trabajos porque una query falló en silencio
es peor que un error visible.

## El template

`Templates/template_ticket_atencion.docx`. Los content controls están bindeados por XPath
contra un Custom XML Part en el namespace `http://Chacomer.TicketAtencion`, con
`storeItemID = {591C03F8-3543-4F11-A238-A80B40C59FFF}`.

Dos reglas que el binding impone y que el código respeta:

1. **El part se sobrescribe, no se reemplaza.** Borrarlo y agregar uno nuevo descarta el
   `CustomXmlPropertiesPart` y con él el `storeItemID` al que apuntan los `w:dataBinding`.
   Word suele tolerarlo cayendo al binding por namespace; la conversión a PDF de Graph no
   siempre.
2. **Todo elemento se emite, aunque venga vacío.** Un elemento ausente deja al control
   mostrando su placeholder.

`TicketDocumentBuilderTests` verifica las dos cosas contra el archivo real que se despliega,
y el pipeline falla si el `.docx` no quedó en el publish output.

## La zona horaria

`ParaguayTime` usa un **offset fijo de UTC-3**, no `TimeZoneInfo`.

Paraguay eliminó el horario de verano a partir de octubre de 2024 y quedó en UTC-3
permanente, pero hay máquinas y contenedores cuya base de zonas todavía trae las reglas
viejas: verificado, `America/Asuncion` devuelve UTC-4 en agosto de 2026 sobre el entorno de
desarrollo. Con la zona del sistema, el mismo código imprimiría una hora distinta según
dónde corra, y sin fallar en ningún lado.

> Si Paraguay vuelve a aplicar horario de verano, esto hay que cambiarlo en el código: no
> se arregla solo con una actualización del sistema operativo.

## Configuración

| Setting | Para qué |
|---|---|
| `DataverseUrl` | Environment de Dataverse. |
| `SharePointSiteUrl` | Sitio donde viven los documentos. **Sin esto la rama de PDF falla siempre.** |
| `GraphClientId` / `GraphTenantId` / `GraphClientSecretName` | Auth contra Graph. Hoy el mismo app registration que Dataverse; se emiten aparte para poder separarlos sin tocar código. |

Los `*ClientSecret` salen del Key Vault, nunca de un app setting — ver
[Secretos y Key Vault](../plataforma/secretos-y-key-vault.md).

## CORS

Es la única app de la EiP con CORS configurado: el `fetch` sale del dominio de Dataverse.
Lo declara el parámetro `allowedOrigins` de `functionApp.bicep`, que en `main.bicep` sale de
`dataverseOrigin`.

Los headers **no** se emiten desde el código. Hacer las dos cosas duplica
`Access-Control-Allow-Origin` y el browser rechaza la respuesta igual que si no hubiera
ninguno.

## Estado del despliegue

`fa-axxonticketatencion-inte` ya existía **creada a mano** —es la que consume el web
resource— así que entra en el mismo cutover que las otras apps de INTE. Tiene su propio
toggle `deployTicketAtencionApp` porque en TEST no existe, y sin él un deployment la crearía
de rebote. Ver [Ambientes](../plataforma/ambientes.md).

Ya aplicado sobre la app de INTE: los app settings nuevos y el CORS. La Managed Identity ya
tenía `Key Vault Secrets User` sobre `keyvaultinte` de antes.

Pendiente:

| # | Qué | Quién |
|---|---|---|
| 1 | Runtime `dotnet-isolated 8.0` → `10.0` | Justo antes de correr el pipeline: el bump rompe el código viejo hasta que entre el nuevo. |
| 2 | Consentimiento de admin de `Sites.ReadWrite.All` y `Files.ReadWrite.All` | Un Global Admin. Ver abajo. |
| 3 | Ubicación de documentos de `msauto_serviceappointment` en Dataverse | Se crea sola al abrir una vez la pestaña Archivos de una Cita. |

### Por qué el PDF nunca funcionó

Dos causas, ninguna en el código de generación del documento:

1. **`SHAREPOINT_SITE_URL` nunca existió como app setting.** La implementación anterior
   armaba `new Uri("")`, tiraba, y el `catch` de la rama best-effort se lo comía. Ya
   corregido.
2. **El app registration `145fd64d` pide `Sites.ReadWrite.All` y `Files.ReadWrite.All` pero
   su service principal tiene cero `appRoleAssignments`**: nadie consintió esos permisos.
   Graph responde 403. **Sigue pendiente** — lo tiene que otorgar un Global Admin.

Mientras el punto 2 siga abierto, la function responde `OK_SIN_PDF`. Eso es el
comportamiento correcto, no un bug.

## Fuera de este repo

| Componente | Dónde | Rol |
|---|---|---|
| `form.js` | `Chacomer Dataverse` → `WebResources/axx_/ServiceAppointment/` | El botón. Falta que mande la function key y que use `wordBase64` cuando `url` viene vacía. |
| `SetReceptionDateTimePlugin` | `Chacomer Dataverse` → `Plugins/…SetReceptionDateTime/` | Setea `axx_receptiondatetime`. Filtra por `msauto_statuscode` mientras el web resource lee `a365_status` para el mismo valor: confirmar cuál cambia realmente antes de dar por buena la fecha de recepción. |
