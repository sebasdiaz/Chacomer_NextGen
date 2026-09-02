<!-- wiki-meta
sources_new:
  - src/integrations/*/*/*.csproj
last_reviewed: 2026-09-01
-->

# Integraciones

Una página por dominio. Cada una corre en su propia Function App y se despliega con su
propio [pipeline](plataforma/pipelines.md).

| Integración | Dirección | Disparador | Function App |
|---|---|---|---|
| [Contacts](integraciones/contacts.md) | Dataverse ↔ Dataverse (+ salida a F&O) | Service Bus | `fa-axxoncontacts-{env}` |
| [Customers](integraciones/customers.md) | Dataverse → F&O | Service Bus | `fa-axxoncustomers-{env}` |
| [Customer credit](integraciones/customercredit.md) | F&O → HTTP | HTTP | `fa-axxoncustomercredit-{env}` (sin crear) |
| [Customer data](integraciones/customerdata.md) | Dataverse → HTTP | HTTP | `fa-axxoncustomerdata-{env}` |
| [Customer groups](integraciones/customergroups.md) | F&O → Dataverse | Timer (diario 23:00) | `fa-axxoncustomergroups-{env}` |
| [Products](integraciones/products.md) | F&O → Dataverse | Timer (diario + horario) | `fa-axxonproducts-{env}` |
| [Fiscal](integraciones/fiscal.md) | SET/DNIT y TURUC → HTTP | HTTP | `fa-axxonfiscal-{env}` |
| [Thinkchat](integraciones/thinkchat.md) | Thinkchat ↔ Dataverse | Timer (cada 2 h) + HTTP | `fa-axxonthinkchat-{env}` |
| [Ticket de Atención](integraciones/ticketatencion.md) | Dataverse → Word/PDF → SharePoint | HTTP (botón en el formulario de Cita) | `fa-axxonticketatencion-{env}` |

## Inventario de funciones

Todas las Azure Functions con su disparador, y la cola o el CRON ya resuelto desde el Bicep:
**[Inventario de funciones](_generado/funciones.md)**. Se genera desde el código, así que no
puede quedar desactualizado.

Ver también [Application Settings por app](_generado/app-settings.md) y
[Colas del Service Bus](_generado/colas.md).
