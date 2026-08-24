<!-- wiki-meta
sources:
  - src/integrations/fiscal/**
  - src/core/Axxon.Eip.Core/Fiscal/**
  - pipelines/azure-pipelines-fiscal.yml
last_reviewed: 2026-08-21
-->

# Fiscal — consultas SET/DNIT y TURUC

Azure Function (.NET 10 isolated) que expone las consultas fiscales de Paraguay como
endpoints HTTP. Es un **proxy puro**: no toca Dataverse, no toca F&O y no consume Service
Bus. Por eso escala libre (`maxInstanceCount = 40`), a diferencia de las apps que llaman a
F&O — ver [Infraestructura › Scale-out](../plataforma/infraestructura.md#scale-out-y-límites-de-fo).

Se separó en su propia app (#50) justamente para sacar esta superficie pública del backbone
de mensajería.

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

## Los dos servicios

Viven en el core (`Axxon.Eip.Core/Fiscal`), no en la app, porque
[Contacts](contacts.md) también los usa:

| Servicio | Base | Credenciales |
|---|---|---|
| `SetApiService` | `https://servicios.set.gov.py/EsetApiWS/ApiWS/` | API Key — secret **`SetApiKey`** del Key Vault |
| `TurucApiService` | `https://turuc.com.py/api/contribuyente/` | ninguna (API pública) |

`SetApiKey` se resuelve desde Key Vault vía `AddEipCore()`; **no** se pasa como app setting.
Por eso `main.bicep` declara esta app con `appSettings: []`. Ver
[Secretos y Key Vault](../plataforma/secretos-y-key-vault.md).

## Estado del despliegue

`fa-axxonfiscal-inte` **no existe**: la integración es nueva y en INTE el Bicep no crea
Function Apps, así que el pipeline va con `deployToInte: false` y estrena directo en TEST.
Ver [Pipelines](../plataforma/pipelines.md) y [Ambientes](../plataforma/ambientes.md).

## Consumidores

| Quién | Qué usa |
|---|---|
| [`RucValidatorControl`](../webresources.md) (PCF, formulario de contact) | `GET /api/turuc/contribuyente/{ruc}` — el base URL y la function key entran por los parámetros `ApiBaseUrl` y `ApiKey` del control |
| [Contacts](contacts.md) (`SetRucValidationService`) | `SetApiService` directo desde el core, sin pasar por esta app: es el path de mensajería |
