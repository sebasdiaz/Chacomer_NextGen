<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Inventario de funciones

> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está
> mal, el que está mal es el código — o el generador. No edites esta página.

Las 18 Azure Functions de la plataforma, con el disparador que declara cada una.
Entre paréntesis, el placeholder tal como está en el atributo; antes, el valor que le
asigna `infra/main.bicep`.

| Function | App | Trigger | Cola / CRON / Ruta |
|---|---|---|---|
| `AccountMasterMatchingFunction` | `fa-axxoncontacts-{env}` | Service Bus | `account-master-matching` _(%AccountServiceBusQueueName%)_ — sessions: no |
| `ContactMasterMatchingFunction` | `fa-axxoncontacts-{env}` | Service Bus | `contact-master-matching` _(%ServiceBusQueueName%)_ — sessions: no |
| `CustomerFoSyncFunction` | `fa-axxoncustomers-{env}` | Service Bus | `customer-fo-sync` _(%FoSyncServiceBusQueueName%)_ — sessions: si |
| `QualifyLeadCustomerSyncFunction` | `fa-axxoncustomers-{env}` | Service Bus | `leadcontacts` _(%ServiceBusQueueName%)_ — sessions: no |
| `CustomerGroupSyncFunction` | `fa-axxoncustomergroups-{env}` | Timer | `0 0 23 * * *` _(%Schedules:CustomerGroupSync%)_ |
| `ProductGroupSyncFunction` | `fa-axxonproducts-{env}` | Timer | `0 0 23 * * *` _(%Schedules:ProductGroupSync%)_ |
| `ReleasedProductSyncFunction` | `fa-axxonproducts-{env}` | Timer | `0 0 * * * *` _(%Schedules:ReleasedProductSync%)_ |
| `ThinkchatTemplateSyncFunction` | `fa-axxonthinkchat-{env}` | Timer | `0 0 */2 * * *` _(%Schedules:ThinkchatTemplateSync%)_ |
| `Set_ConsultaRuc` | `fa-axxonfiscal-{env}` | HTTP | GET /api/set/consulta-ruc — auth: Function |
| `Set_Options` | `fa-axxonfiscal-{env}` | HTTP | OPTIONS /api/set/{*any} — auth: Anonymous |
| `Set_ValidezDocumentoMaquinaRegistradora` | `fa-axxonfiscal-{env}` | HTTP | GET /api/set/validez-documento-maquina-registradora — auth: Function |
| `Set_ValidezDocumentoTimbrado` | `fa-axxonfiscal-{env}` | HTTP | GET /api/set/validez-documento-timbrado — auth: Function |
| `Turuc_GetContribuyente` | `fa-axxonfiscal-{env}` | HTTP | GET /api/turuc/contribuyente/{ruc} — auth: Function |
| `Turuc_GetContribuyenteTable` | `fa-axxonfiscal-{env}` | HTTP | GET /api/turuc/contribuyente/table — auth: Function |
| `Turuc_GetEntidadPublica` | `fa-axxonfiscal-{env}` | HTTP | GET /api/turuc/entidad-publica — auth: Function |
| `Turuc_GetPersonaJuridica` | `fa-axxonfiscal-{env}` | HTTP | GET /api/turuc/persona-juridica — auth: Function |
| `Turuc_SearchContribuyentes` | `fa-axxonfiscal-{env}` | HTTP | GET /api/turuc/contribuyente/search — auth: Function |
| `GenerarTicketAtencion` | `fa-axxonticketatencion-{env}` | HTTP | POST /api/GenerarTicketAtencion — auth: Function |
