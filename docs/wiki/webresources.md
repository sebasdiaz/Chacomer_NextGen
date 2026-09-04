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

**`RucValidatorControl`** — bound al campo `DocumentNumber`. Está puesto en cinco lugares:
tres formularios de contact, uno de account y el **`Annata 365` de lead**.

> **En lead el control corre en otro contexto y no todo aplica.** El campo bindeado es
> `axx_numerodocumento`, que guarda **CI o RUC** según `axx_tipodedocumento`; en contact y
> account es `msdyn_identificationnumber`, que siempre es un RUC.
>
> Desde la v1.0.4, **si el tipo de documento no dice "RUC" el control no consulta nada** y
> lo informa. Mandarle una cédula a la SET devolvía "no registrado", que se lee como un dato
> malo cuando la consulta nunca correspondía. La comparación es por la **etiqueta** del
> OptionSet, no por su valor: los números no son estables entre environments. Si el campo no
> está en el formulario —contact, account— no hay nada que saltear y la validación corre
> como siempre.
>
**Desde la v1.2.0 el control escribe un solo campo: `axx_dnitresponse`.** Ya no toca
`governmentid` ni `axx_fiscalstate`.

`governmentid` y `axx_fiscalstate` **no existen en lead**, así que ahí cada validación
terminaba en un warning por campos que en esa entidad no van a existir. Crearlos igual que
en contact no era opción: **`governmentid` es un campo de Microsoft**, no custom, así que
en lead sólo podría existir como `axx_governmentid` — otro nombre y una columna más con el
RUC, que en lead ya vive en `axx_numerodocumento`.

Que dejara de escribir `axx_fiscalstate` no se perdió nada: lo mantiene
`SetRucValidationService` desde [Contacts](integraciones/contacts.md), que ahora es su
**único** escritor. Los dos leían la misma SET, pero no hay orden garantizado entre el
formulario y la cola, así que tener dos sólo definía quién escribía último. De paso, el
mapeo de estados dejó de estar duplicado.

El RUC formateado no se pierde: vuelve por el campo al que el control está bindeado.

**Desde la v1.1.0 la URL vive en una environment variable**, no en los parámetros del
control:

```
axx_FISCAL_CONSULTA_RUC_URL
https://fa-axxonfiscal-inte.azurewebsites.net/api/set/consulta-ruc?code=…
```

El control le agrega `&ruc=..&dv=..`. Mismo criterio que `axx_FUNCTION_URL` de
[TicketAtencion](integraciones/ticketatencion.md): **la key va dentro de la URL**, no en un
header aparte, para tener un solo lugar que tocar cuando rota o cuando cambia el ambiente.
La key llega al browser igual — la variable no la esconde, sólo la centraliza.

Que estuviera en los parámetros del control no era un detalle: **el mismo valor estaba
repetido en 15 lugares** (5 placements × 3 form factors), y ahí se lo lleva puesto cualquier
import de solución. Pasó de verdad: se corrigió el formulario de lead, se verificó, y más
tarde había vuelto solo al valor viejo.

`ApiBaseUrl` y `ApiKey` **siguen existiendo como fallback** y ya no son obligatorios: los usa
un formulario que todavía no migró, y el harness de `pcf-scripts start`, donde no hay
Dataverse. Si no hay ni variable ni parámetro, el control lo dice en vez de fallar callado.

Leer la variable exige `<uses-feature name="WebAPI" required="true" />` en el manifest. Se
lee **una vez por sesión del browser** (cache estático compartido entre instancias), y un
error de esa query no rompe nada: cae al fallback, porque un usuario sin lectura sobre la
tabla de variables tiene que poder seguir validando.

> **El `ApiBaseUrl` apunta a [Fiscal](integraciones/fiscal.md), no a Contacts.** Es fácil
> equivocarse porque el control vive en la solución de contacts: los cinco placements
> apuntaban a `fa-axxoncontacts-inte`, que no tiene **ningún** endpoint HTTP desde que el
> #50 movió las consultas RUC a `AxxonFiscal`. Daba `404` — con TURUC también, así que el
> control estuvo roto desde entonces. Si deja de responder, esto es lo primero que mirar.
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
- **El control ya no parte los nombres, y no es una decisión: es que no se puede.** TURUC
  devolvía `"MIRANDA RUIZ DIAZ, JORGE SEBASTIAN"` — la coma marcaba dónde terminaban los
  apellidos. La SET devuelve `"JORGE SEBASTIAN MIRANDA RUIZ DIAZ"`, sin separador y con los
  nombres primero, así que no hay forma de saber si `JORGE SEBASTIAN MIRANDA` son dos
  nombres y un apellido o un nombre y dos apellidos. El control avisa con el nombre completo
  para que se cargue a mano. Sigue partiendo si algún día viene una coma.

  El tipo de persona sale de **`contribuyente.tipoPersona`**, que vale `"FISICO"` o
  `"JURIDICO"` — verificado contra INTE el 2026-09-03. Si no reconoce el valor, no toca
  `lastname`/`firstname`/`middlename`: partir la razón social de una empresa en apellido y
  nombres deja el contacto con datos falsos que nadie nota.

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
