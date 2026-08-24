<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Controles PCF

> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está
> mal, el que está mal es el código — o el generador. No edites esta página.

Lo que declara cada `ControlManifest.Input.xml`. Qué hace cada control y dónde está puesto
vive en [Web resources](../webresources.md).

| Control | Namespace | Versión | Propiedades | Carpeta |
|---|---|---|---|---|
| `DeviceRegistrationGrid` | `AxxonContacts` | `0.0.1` | `masterContactId (bound, SingleLine.Text)` | `src/webresources/DeviceRegistrationGrid` |
| `DnitResponseViewer` | `AxxonContacts` | `0.0.1` | `dnitResponse (bound, Multiple)` | `src/webresources/DnitResponseViewer` |
| `MasterAccountChildrenGrid` | `AxxonContacts` | `0.0.1` | `masterAccountId (bound, SingleLine.Text)` | `src/webresources/MasterAccountChildrenGrid` |
| `MasterContactAccountGrid` | `AxxonContacts` | `0.0.1` | `masterContactId (bound, SingleLine.Text)` | `src/webresources/MasterContactAccountGrid` |
| `MasterContactAddressesGrid` | `AxxonContacts` | `0.0.2` | `masterContactId (bound, SingleLine.Text)` | `src/webresources/MasterContactAddressesGrid` |
| `MasterContactChildrenGrid` | `AxxonContacts` | `0.0.1` | `masterContactId (bound, SingleLine.Text)` | `src/webresources/MasterContactChildrenGrid` |
| `RucValidatorControl` | `AxxonContacts` | `1.0.1` | `DocumentNumber (bound, SingleLine.Text)`, `ApiBaseUrl (input, SingleLine.URL)`, `ApiKey (input, SingleLine.Text)` | `src/integrations/contacts/AxxonContacts.PCF` |
