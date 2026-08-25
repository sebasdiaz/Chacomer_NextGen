<!-- wiki-meta
sources:
  - src/integrations/service/AxxonTicketAtencion.Functions/**
  - src/core/Axxon.Eip.Core/Graph/**
  - src/core/Axxon.Eip.Core/Dataverse/DataverseWebApiClient.cs
  - tests/AxxonTicketAtencion.Functions.Tests/**
  - pipelines/azure-pipelines-ticketatencion.yml
last_reviewed: 2026-08-25
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

`fa-axxonticketatencion-inte` existía **creada a mano** y era la quinta app pendiente del
[cutover de INTE](../plataforma/ambientes.md). Se borró, así que dejó de ser deuda: nace
administrada por Bicep con su propio toggle `deployTicketAtencionApp`, igual que thinkchat.
`deployFunctionApps` sigue en `false` por las otras cuatro.

**Nace con Managed Identity.** `inte.bicepparam` no declara `dataverseClientId`, así que el
template no emite `DataverseClientId` ni `GraphClientId`: la app autentica con su propia MI
contra Dataverse y contra Graph, sin ningún secreto. Es el estado deseado y el mismo camino
que products y thinkchat.

El what-if contra `DataverseINTE` da **6 Create**: el storage, su blobService y el container
`deploymentpackage`, el plan `asp-fa-axxonticketatencion-inte`, la app y su diagnostic
setting. Usa el Application Insights compartido `appi-eip-inte` y el vault
`kv-chacomer-eip-inte`.

### Pasos de alta

1. **Crear la app** — pipeline `NextGen - infra INTE`, o `az deployment group create` con
   `inte.bicepparam`.
2. **Los 3 roles de la MI, a mano.** `deployRoleAssignments` está en `false` en INTE, así
   que la app nace sin ellos. **No es opcional**: `AzureWebJobsStorage` va por identidad, así
   que sin los roles de Storage la app ni siquiera arranca. Receta y el fallback cuando el
   CLI falla, en [Ambientes › apps de INTE con los roles a mano](../plataforma/ambientes.md#inte-las-apps-que-nacen-con-los-roles-a-mano).
3. **La MI como Application User en Dataverse INTE** — ver abajo.
4. **Los app roles de Graph asignados a la MI** — ver abajo.
5. **Desplegar el código** con el pipeline de la integración.

### Los dos GUID de la managed identity, y cuál va en cada lado

Es el error clásico: son dos identificadores distintos y el PPAC pide el que uno no espera.

```bash
MI=$(az functionapp identity show -g DataverseINTE -n fa-axxonticketatencion-inte --query principalId -o tsv)
az ad sp show --id $MI --query "{objectId:id, appId:appId, displayName:displayName}" -o json
```

| Valor | Para qué |
|---|---|
| **Application (client) ID** (`appId`) | **El Application User en Dataverse.** Es el que pide el PPAC. |
| Object ID / `principalId` | Los role assignments de Azure y los app roles de Graph. |

> **No los hardcodees.** Son de *esta* instancia de la app: si se borra y se recrea, la
> managed identity es nueva y los dos GUID cambian — hay que rehacer el app user, los tres
> role assignments y los app roles de Graph. Es exactamente lo que pasó el 2026-08-24 cuando
> se borró la app creada a mano. Es otra razón para que la app quede administrada por Bicep
> y no se toque más a mano.

### El Application User en Dataverse

PPAC → **Environments → b1-chacomer-inte → Settings → Users + permissions → Application
users → + New app user** → *Add an app* → pegar el **Application ID** → business unit → rol
de seguridad.

Si el buscador no encuentra la app por nombre, pegar el GUID directo: las managed identities
no siempre aparecen listadas.

El rol de seguridad tiene que cubrir seis lecturas y una sola escritura:

| Tabla | Acceso |
|---|---|
| `msauto_serviceappointment` | Read |
| `msauto_device` y sus lookups (marca, modelo, color, código de producto) | Read |
| `contact` / `account` / `customeraddress` | Read |
| `cdm_company` | Read |
| `msauto_serviceorderjob` | Read |
| `a365_externalnote` | Read |
| `msauto_devicemeasurement` | Read |
| **`sharepointdocumentlocation`** | **Read + Create** |

El único write es el `sharepointdocumentlocation`, y sólo cuando la Cita todavía no tiene
carpeta de documentos.

### Los permisos de Graph

`Sites.ReadWrite.All` y `Files.ReadWrite.All`, como permisos de **aplicación**, asignados a
la managed identity de la app. Para managed identities **no hay botón de "Grant admin
consent" en el portal**: van por Graph API, y los tiene que otorgar un Global Admin.

```bash
MI=$(az functionapp identity show -g DataverseINTE -n fa-axxonticketatencion-inte --query principalId -o tsv)
GRAPH=$(az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv)
for ROL in 9492366f-7969-46a4-8d15-ed1a20078fff 75359482-378d-4052-8f01-80520e7db3cd; do
  az rest --method post \
    --url "https://graph.microsoft.com/v1.0/servicePrincipals/$MI/appRoleAssignments" \
    --headers "Content-Type=application/json" \
    --body "{\"principalId\":\"$MI\",\"resourceId\":\"$GRAPH\",\"appRoleId\":\"$ROL\"}"
done
```

Asignarlos a la MI y no al app registration compartido `145fd64d` tiene una ventaja
concreta: `Sites.ReadWrite.All` es **tenant-wide**, y colgado del registration compartido le
daría escritura sobre todo SharePoint a la identidad que usan las otras seis apps de la EiP.

Hasta que estén, Graph responde 403 y el ticket sale con `status = OK_SIN_PDF`: el usuario
obtiene igual su Word. Es el comportamiento correcto, no un bug.

### Por qué el PDF nunca funcionó en la versión anterior

Dos causas, ninguna en el código de generación del documento:

1. **`SHAREPOINT_SITE_URL` nunca existió como app setting.** La implementación anterior
   armaba `new Uri("")`, tiraba, y el `catch` de la rama best-effort se lo comía. La versión
   nueva lo recibe del Bicep (`sharePointSiteUrl`).
2. **Nadie consintió los permisos de Graph.** El registration los pedía, pero su service
   principal tenía cero `appRoleAssignments`.

### Huérfanos de la app borrada

Quedaron tres recursos sin dueño en `DataverseINTE`, que el Bicep no administra y no pisa:
el storage `dataverseinteticket`, el Application Insights `fa-axxonticketatencion-inte` (la
app nueva usa el compartido) y su alert rule `Failure Anomalies - …`. Se pueden borrar una
vez validada la app nueva; nada los referencia.

## Fuera de este repo

| Componente | Dónde | Rol |
|---|---|---|
| `form.js` | `Chacomer Dataverse` → `WebResources/axx_/ServiceAppointment/` | El botón. Falta que mande la function key y que use `wordBase64` cuando `url` viene vacía. |
| `SetReceptionDateTimePlugin` | `Chacomer Dataverse` → `Plugins/…SetReceptionDateTime/` | Setea `axx_receptiondatetime`. Filtra por `msauto_statuscode` mientras el web resource lee `a365_status` para el mismo valor: confirmar cuál cambia realmente antes de dar por buena la fecha de recepción. |
