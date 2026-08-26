<!-- wiki-meta
sources:
  - src/integrations/fiscal/**
  - src/core/Axxon.Eip.Core/Fiscal/**
  - pipelines/azure-pipelines-fiscal.yml
last_reviewed: 2026-08-26
-->

# Fiscal — consultas por RUC (SET/DNIT, TURUC y Dataverse)

Azure Function (.NET 10 isolated) que expone las consultas fiscales de Paraguay como
endpoints HTTP. No toca F&O y no consume Service Bus, así que escala libre
(`maxInstanceCount = 40`) a diferencia de las apps que llaman a F&O — ver
[Infraestructura › Scale-out](../plataforma/infraestructura.md#scale-out-y-límites-de-fo).

Se separó en su propia app (#50) justamente para sacar esta superficie pública del backbone
de mensajería.

Nació como proxy puro hacia SET y TURUC. `Dataverse_ConsultaRuc` rompió esa pureza: es el
único endpoint cuyo origen es Dataverse y no una API externa. Se dejó acá igual porque la
app ya es *la* superficie de consulta por RUC, y porque la alternativa —meterlo en
[Contacts](contacts.md)— le colgaba una API pública a una app de mensajería con
`foBoundMaxInstanceCount = 1`. El costo de la decisión es que esta app dejó de ser
apátrida: ahora necesita `DataverseUrl` y su MI dada de alta en Dataverse.

## Endpoints

Todos con `AuthorizationLevel.Function` (hace falta la function key), salvo el preflight de
CORS.

| Function | Ruta | Origen |
|---|---|---|
| `Set_ConsultaRuc` | `GET /api/set/consulta-ruc?ruc=XX&dv=Y` | SET |
| `Set_ValidezDocumentoTimbrado` | `GET /api/set/validez-documento-timbrado` | SET |
| `Set_ValidezDocumentoMaquinaRegistradora` | `GET /api/set/validez-documento-maquina-registradora` | SET |
| `Set_Options` | `OPTIONS /api/set/{*any}` — preflight CORS, anónimo | — |
| `Turuc_GetContribuyente` | `GET /api/turuc/contribuyente/{ruc}` | TURUC |
| `Turuc_SearchContribuyentes` | `GET /api/turuc/contribuyente/search` | TURUC |
| `Turuc_GetContribuyenteTable` | `GET /api/turuc/contribuyente/table` | TURUC |
| `Turuc_GetPersonaJuridica` | `GET /api/turuc/persona-juridica` | TURUC |
| `Turuc_GetEntidadPublica` | `GET /api/turuc/entidad-publica` | TURUC |
| `Dataverse_ConsultaRuc` | `GET /api/dataverse/consulta-ruc?ruc=XX` | Dataverse |
| `Dataverse_Options` | `OPTIONS /api/dataverse/{*any}` — preflight CORS, anónimo | — |

## Consulta de partes en Dataverse

`Dataverse_ConsultaRuc` busca **contacts y accounts** por RUC y devuelve, de cada uno,
nombre, identification number y tipo de persona.

Busca contra **`msdyn_identificationnumber`** — el mismo campo sobre el que
[Contacts](contacts.md) matchea masters y el que valida la SET. No contra `governmentid`,
que es donde el `RucValidatorControl` deja el RUC en el formulario: los dos conviven, pero
el que tiene el dato completo en account y contact es `msdyn_identificationnumber`.

El RUC se acepta con o sin dígito verificador: `80054203-7` matchea por igualdad y
`80054203` por prefijo `80054203-`. El guion del prefijo es lo que evita que `8005420`
arrastre registros ajenos.

**El tipo de persona se deriva de la tabla**, no de un campo: `account` → `"Juridica"`,
`contact` → `"Fisica"`. No hay OptionSet de personería en el modelo. Si algún día lo hay,
este es el lugar donde cambia.

Un RUC devuelve normalmente **varias filas**: el master más los raws que cuelgan de él, uno
por legal entity. Por eso la respuesta es una lista con `esMaster` en cada ítem, y no un
registro único. Vienen los accounts primero y, dentro de cada tabla, el master antes que
los raws. Tope de 50 por tabla.

```json
{
  "ruc": "80054203-7",
  "cantidad": 2,
  "resultados": [
    {
      "id": "0f8c…",
      "entidad": "account",
      "tipoPersona": "Juridica",
      "nombre": "ACME SA",
      "identificationNumber": "80054203-7",
      "esMaster": true
    }
  ]
}
```

Es **de solo lectura**: no crea, no actualiza y no publica mensajes. Un error de Dataverse
vuelve como `502` con el detalle sólo en el log, nunca en el body.

A diferencia de [Contacts](contacts.md), que crea un `ServiceClient` por invocación, acá el
cliente es **singleton**: los triggers son HTTP y corren en paralelo dentro de la instancia,
así que uno por request pagaría el handshake de auth en cada llamada. La justificación del
Transient en Contacts (`maxConcurrentCallsPerSession = 1` de las sessions de Service Bus)
no aplica a HTTP.

## Los dos servicios

Viven en el core (`Axxon.Eip.Core/Fiscal`), no en la app, porque
[Contacts](contacts.md) también los usa:

| Servicio | Base | Credenciales |
|---|---|---|
| `SetApiService` | `https://servicios.set.gov.py/EsetApiWS/ApiWS/` | API Key — secret **`SetApiKey`** del Key Vault |
| `TurucApiService` | `https://turuc.com.py/api/contribuyente/` | ninguna (API pública) |

`SetApiKey` se resuelve desde Key Vault vía `AddEipCore()`; **no** se pasa como app setting.
El único app setting que declara `main.bicep` para esta app es `DataverseUrl`. Ver
[Secretos y Key Vault](../plataforma/secretos-y-key-vault.md) y
[Application Settings](../_generado/app-settings.md).

Dataverse va por **Managed Identity** (sin `dataverseAuthSettings`, mismo criterio que
products y thinkchat). Antes del primer `Dataverse_ConsultaRuc` hay que dar de alta la MI
de la app como **Application User** en el environment, con un rol de seguridad que le deje
leer contact y account. Sin eso el endpoint devuelve `502`.

## Estado del despliegue

La app va a **los dos ambientes**: `fa-axxonfiscal-test` y `fa-axxonfiscal-inte`, con el
pipeline en `deployToInte: true`.

INTE llegó después. La app es **greenfield** —nunca existió creada a mano—, así que no
arrastra el problema de adopción que mantiene `deployFunctionApps = false` en INTE por las
otras cuatro apps. Por eso tiene su propio toggle, `deployFiscalApp`, prendido en
`inte.bicepparam`; mismo criterio que thinkchat y ticketatencion.

**El orden importa**: `fa-axxonfiscal-inte` lo crea el pipeline de infra, no este. Hasta que
ese deployment no corrió, este pipeline falla al desplegar sobre una app inexistente.

Y como INTE va con `deployRoleAssignments = false`, la app **nace sin sus roles y no
arranca**: `AzureWebJobsStorage` va por identidad. Los tres role assignments van a mano
después del deploy — comandos en
[Ambientes › las apps que nacen con los roles a mano](../plataforma/ambientes.md#inte-las-apps-que-nacen-con-los-roles-a-mano).

| Paso | Quién lo hace | Sin esto |
|---|---|---|
| Crear la app | pipeline de infra (`deployFiscalApp = true`) | el deploy de la app falla |
| Storage Blob Data Owner + Storage Queue Data Contributor + Key Vault Secrets User | a mano sobre la MI | la app **no arranca** |
| MI como Application User en Dataverse INTE (lectura de contact y account) | a mano en el PPAC | `Dataverse_ConsultaRuc` responde `502`; SET/TURUC andan igual |

`SetApiKey` ya está en `kv-chacomer-eip-inte`, así que los endpoints de la SET no necesitan
nada extra una vez que la MI tiene Key Vault Secrets User.

Ver [Pipelines](../plataforma/pipelines.md) y [Ambientes](../plataforma/ambientes.md).

## Consumidores

| Quién | Qué usa |
|---|---|
| [`RucValidatorControl`](../webresources.md) (PCF, formulario de contact) | `GET /api/turuc/contribuyente/{ruc}` — el base URL y la function key entran por los parámetros `ApiBaseUrl` y `ApiKey` del control |
| [Contacts](contacts.md) (`SetRucValidationService`) | `SetApiService` directo desde el core, sin pasar por esta app: es el path de mensajería |

## Pendiente de documentar

- Quién consume `Dataverse_ConsultaRuc`. Se construyó a pedido, sin un caller identificado
  todavía.
