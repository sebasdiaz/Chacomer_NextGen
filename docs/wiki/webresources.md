<!-- wiki-meta
sources:
  - src/webresources/*/*/ControlManifest.Input.xml
  - src/integrations/contacts/AxxonContacts.PCF/*/ControlManifest.Input.xml
sources_new:
  - src/webresources/*/*/*.tsx
  - src/integrations/contacts/AxxonContacts.WebResources/**
last_reviewed: 2026-09-03
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
Consulta `GET {ApiBaseUrl}/api/set/consulta-ruc?ruc={ruc}&dv={dv}` contra la app
[Fiscal](integraciones/fiscal.md) (base URL y function key entran como parámetros del
control) y escribe el resultado en el formulario, mapeando el estado al OptionSet
`axx_fiscalstate`.

**Consultaba TURUC hasta la v1.0.1.** Se pasó a la SET porque es la fuente oficial: es la
misma que usa el path de mensajería (`SetRucValidationService`), así que el formulario y el
matching por Service Bus dejan de poder discrepar sobre el estado de un mismo RUC.

Tres consecuencias del cambio que no se leen del diff:

- **El RUC hay que escribirlo con dígito verificador.** La SET pide `ruc` y `dv` por
  separado, así que el control parte el valor por el guion y corta con un error si no lo
  encuentra. TURUC aceptaba el número solo.
- **El JSON crudo ahora va a `axx_dnitresponse`**, no a `description`. Es el campo que
  renderiza el `DnitResponseViewer` y el que escribe `SetRucValidationService`: dejarlo en
  `description` hubiera guardado una respuesta de la SET donde nadie la parsea.
- **Los nombres se parten sólo si la SET dice que es persona física.** TURUC traía
  `esPersonaJuridica`/`esEntidadPublica`; la SET trae `tipoContribuyente`, texto libre del
  que no tenemos la lista cerrada de valores. Si no dice "FISICA" ni "JURIDICA", el control
  **no toca** `lastname`/`firstname`/`middlename` y avisa: partir la razón social de una
  empresa en apellido y nombres deja el contacto con datos falsos que nadie nota.

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
