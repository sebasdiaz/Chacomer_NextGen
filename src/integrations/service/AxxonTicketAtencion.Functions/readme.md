# AxxonTicketAtencion.Functions

Genera el **Ticket de Atención** (Orden de Reparación) de una Cita de Servicio en Word,
lo devuelve para que el navegador lo abra, y en paralelo lo convierte a PDF y lo adjunta
a la Cita en SharePoint.

GAP-103 / GAP-227. Reemplaza a `AzureFunctions/TicketAtencion/` del repo
**Chacomer Dataverse**, que queda a deprecar.

## Cadena completa

```
[D365] Formulario de Cita de Servicio (msauto_serviceappointment)
   │  botón "Generar Ticket Atención"
   ▼
[Web Resource] axx_/ServiceAppointment/form.js        ← repo Chacomer Dataverse
   │  POST { serviceAppointmentId } + x-functions-key
   ▼
[Azure Function] GenerarTicketAtencion                ← este proyecto
   ├─► Dataverse Web API v9.2      (6 queries)
   ├─► template_ticket_atencion.docx (Custom XML Part)
   └─► Microsoft Graph              (PDF + upload a SharePoint)
   │
   ▼  { status, wordBase64, fileName, url, wordBytes }
[Web Resource] abre el documento
```

## Endpoint

```
POST /api/GenerarTicketAtencion
x-functions-key: <key>
{ "serviceAppointmentId": "0f8a…" }
```

| Status | Cuándo |
|---|---|
| `200` + `status: "OK"` | Word generado y PDF adjuntado. |
| `200` + `status: "OK_SIN_PDF"` | Word generado; falló el PDF o SharePoint. **`url` viene vacía: el cliente debe usar `wordBase64`.** |
| `400` | Body vacío, no-JSON, sin `serviceAppointmentId` o con un GUID malformado. |
| `401` | Sin function key. Lo resuelve el host, no este código. |
| `404` | La Cita no existe. |
| `500` | Falla interna. |

Los errores devuelven `{ "status": "ERROR", "mensaje": "…" }` con un texto apto para
mostrarle al usuario. **Ningún response lleva stack traces**: el detalle va a Application
Insights.

## Estructura

| Pieza | Rol |
|---|---|
| `Functions/GenerarTicketAtencionFunction.cs` | Sólo orquesta: valida el input, arma la respuesta, aísla la rama de SharePoint. |
| `Services/TicketAtencionDataService.cs` | Las 6 queries a Dataverse. |
| `Services/TicketXmlBuilder.cs` | Arma el XML del Custom XML Part. Puro, sin red. |
| `Services/TicketSharePointService.cs` | PDF + upload + `sharepointdocumentlocation`. |
| `Services/ParaguayTime.cs` | Formato de fecha en hora de Paraguay. |
| `Documents/TicketDocumentBuilder.cs` | Relleno del `.docx` con OpenXML. |
| `Templates/template_ticket_atencion.docx` | Plantilla con los content controls bindeados. |

Lo cross vive en `Axxon.Eip.Core`: `AddEipDataverseWebApi` (cliente OData de Dataverse) y
`AddEipGraph` (Graph/SharePoint). Nada de eso es específico del ticket.

## El template

Los content controls están bindeados por XPath contra el namespace
`http://Chacomer.TicketAtencion`, con `storeItemID = {591C03F8-3543-4F11-A238-A80B40C59FFF}`.

Dos cosas que **no** se pueden hacer:

1. **Borrar el Custom XML Part y agregar uno nuevo.** Eso descarta el
   `CustomXmlPropertiesPart` y con él el `storeItemID` al que apuntan los `w:dataBinding`.
   Word suele tolerarlo cayendo al binding por namespace; la conversión a PDF de Graph no
   siempre. Se sobrescribe el part existente con `FeedData`.
2. **Omitir un elemento del XML.** El binding espera todos presentes; un elemento ausente
   deja al control mostrando su placeholder. Un campo sin dato va como elemento vacío.

`TicketDocumentBuilderTests` verifica las dos cosas contra el archivo real.

## App settings

| Setting | Valor | Notas |
|---|---|---|
| `DataverseUrl` | `https://operations-b1-chacomer-inte.crm.dynamics.com` | |
| `SharePointSiteUrl` | `https://chacomercompy.sharepoint.com/sites/B1-Chacomer-INTE` | Sin esto la rama de PDF falla siempre. |
| `KeyVaultUri` | `https://keyvaultinte.vault.azure.net/` | Lo monta `AddEipCore()`. |
| `DataverseClientId` | `145fd64d-…` | Vacío = Managed Identity. |
| `DataverseTenantId` | `d0e6feed-…` | Sólo con client secret: `ClientSecretCredential` pide la authority explícita. |
| `DataverseClientSecretName` | `SecretNextGenDynamics365Inte` | Indirección al nombre real del secret en el vault. |
| `GraphClientId` | `145fd64d-…` | Hoy el mismo registration que Dataverse. |
| `GraphTenantId` | `d0e6feed-…` | |
| `GraphClientSecretName` | `SecretNextGenDynamics365Inte` | |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | del recurso de App Insights | |

Ningún secreto va en app settings: salen del Key Vault por el provider de configuración
que monta `AddEipCore()`. Ver `EipSecretResolver` para la indirección `{Clave}Name`.

## Estado en INTE — lo que falta antes del primer deploy

Verificado sobre `fa-axxonticketatencion-inte` (RG `DataverseINTE`) el 2026-08-24.

Ya aplicado:

| Hecho | Detalle |
|---|---|
| App settings nuevos | `KeyVaultUri`, `DataverseUrl`, `DataverseTenantId`, `DataverseClientSecretName`, `Graph*` y `SharePointSiteUrl`. La ausencia de este último es la razón por la que el PDF nunca funcionó: la implementación anterior armaba `new Uri("")`, tiraba, y el `catch` se lo comía. |
| Rol de Key Vault | La Managed Identity **ya tenía** `Key Vault Secrets User` sobre `keyvaultinte` desde antes. |
| CORS | Origen `https://operations-b1-chacomer-inte.crm.dynamics.com`. |

Pendiente:

| # | Pendiente | Por qué bloquea |
|---|---|---|
| 1 | **Runtime en `dotnet-isolated 8.0`** | Este proyecto es `net10.0`. Hay que subirlo a `10.0` o el worker no levanta. Conviene hacerlo justo antes de correr el pipeline: el bump rompe el código viejo hasta que entre el nuevo. |
| 2 | **Consentimiento de admin de los permisos de Graph** | El registration `145fd64d` pide `Sites.ReadWrite.All` y `Files.ReadWrite.All`, pero su service principal tiene **cero** `appRoleAssignments`: nadie consintió. Sin eso Graph responde 403 y el ticket sale siempre `OK_SIN_PDF`. **Lo tiene que otorgar un Global Admin.** |
| 3 | **La ubicación de documentos de `msauto_serviceappointment`** | `TicketSharePointService` la necesita como padre. Se crea sola al abrir una vez la pestaña Archivos de una Cita. |
| 4 | **`AZURE_CLIENT_SECRET` en texto plano** | Se borra después de validar el deploy, junto con `DATAVERSE_URL`, `AZURE_TENANT_ID` y `AZURE_CLIENT_ID`, que ya no los lee nadie. Siguen puestos a propósito: son los que usa el código viejo mientras siga desplegado. |

Comandos en `infra/README.md`, sección de INTE.

## Desarrollo local

```bash
cp local.settings.example.json local.settings.json
```

Completar el client secret. Con `az login` hecho y sin `ClientId`/`ClientSecret`,
`DefaultAzureCredential` usa la identidad del usuario — que sirve para Dataverse si está
dado de alta, pero **no** para los permisos de aplicación de Graph.

```bash
dotnet test ../../../../tests/AxxonTicketAtencion.Functions.Tests
```

Los tests no tocan la red: cubren el XML, el relleno del `.docx` contra el template real y
el formato de fecha. `Deja_un_ejemplar_inspeccionable_en_la_salida_del_test` deja un
`ticket-atencion-ejemplo.docx` en `bin/` para abrirlo en Word.
