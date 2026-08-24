<!-- wiki-meta
sources:
  - src/webresources/*/*/ControlManifest.Input.xml
  - src/integrations/contacts/AxxonContacts.PCF/*/ControlManifest.Input.xml
sources_new:
  - src/webresources/*/*/*.tsx
  - src/integrations/contacts/AxxonContacts.WebResources/**
last_reviewed: 2026-08-21
-->

# Web resources y controles PCF

Siete controles PCF, todos en el namespace **`AxxonContacts`**. Se compilan con `npm` +
`pac pcf` y viajan a Dataverse dentro de la solución, **no** por los pipelines de Function
Apps: hoy el import de la solución es manual.

Versiones, propiedades y carpeta de cada uno:
**[Controles PCF](_generado/controles-pcf.md)** (generado desde los `ControlManifest`).

Lo que el manifest no dice — de dónde saca los datos cada control:

| Control | Lee de Dataverse |
|---|---|
| `RucValidatorControl` | — (llama a [Fiscal](integraciones/fiscal.md)) |
| `MasterContactChildrenGrid` | `contact` |
| `MasterContactAccountGrid` | `account` |
| `MasterAccountChildrenGrid` | `account` |
| `MasterContactAddressesGrid` | `customeraddress` |
| `DeviceRegistrationGrid` | `msauto_deviceregistration` |
| `DnitResponseViewer` | — (renderiza el JSON del campo `dnitResponse`) |

## Los que ya tienen contexto

**`RucValidatorControl`** — bound al campo `DocumentNumber` del formulario de contact.
Consulta `GET {ApiBaseUrl}/api/turuc/contribuyente/{ruc}` contra la app
[Fiscal](integraciones/fiscal.md) (base URL y function key entran como parámetros del
control) y escribe el resultado en el formulario, mapeando el estado de la API al OptionSet
`axx_fiscalstate`.

**`MasterContactAddressesGrid`** — muestra los domicilios (`customeraddress`) de los
contactos hijo de un Master Contact. Es la contraparte visual del copiado de domicilio al
master que hace [Contacts](integraciones/contacts.md).

**`DnitResponseViewer`** — no consulta nada: recibe el JSON crudo de la respuesta de DNIT
por el parámetro `dnitResponse` y lo renderiza legible.

## Pendiente de documentar

- En qué formulario y con qué configuración está puesto cada grid.
- Cómo se versiona y se importa la solución que los contiene (hoy es manual y no está en
  ningún pipeline).
- `src/integrations/contacts/AxxonContacts.WebResources/` — web resources clásicos (JS/HTML),
  sin documentar.
