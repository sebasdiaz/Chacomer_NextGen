<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Matriz de pipelines

> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está
> mal, el que está mal es el código — o el generador. No edites esta página.

Lo que declara `pipelines/*.yml`. El detalle de cómo funciona la promoción está en
[Pipelines](../plataforma/pipelines.md).

## Integraciones

| Pipeline | App base | INTE | TEST | Tests |
|---|---|---|---|---|
| [azure-pipelines-contacts.yml](../../../pipelines/azure-pipelines-contacts.yml) | `fa-axxoncontacts` | si | si | — |
| [azure-pipelines-customergroups.yml](../../../pipelines/azure-pipelines-customergroups.yml) | `fa-axxoncustomergroups` | `fa-axxoncustomergroup` | si | — |
| [azure-pipelines-customers.yml](../../../pipelines/azure-pipelines-customers.yml) | `fa-axxoncustomers` | si | si | `tests/AxxonCustomers.Functions.Tests/AxxonCustomers.Functions.Tests.csproj` |
| [azure-pipelines-fiscal.yml](../../../pipelines/azure-pipelines-fiscal.yml) | `fa-axxonfiscal` | no | si | — |
| [azure-pipelines-products.yml](../../../pipelines/azure-pipelines-products.yml) | `fa-axxonproducts` | si | si | — |
| [azure-pipelines-thinkchat.yml](../../../pipelines/azure-pipelines-thinkchat.yml) | `fa-axxonthinkchat` | si | no | — |
| [azure-pipelines-ticketatencion.yml](../../../pipelines/azure-pipelines-ticketatencion.yml) | `fa-axxonticketatencion` | si | no | `tests/AxxonTicketAtencion.Functions.Tests/AxxonTicketAtencion.Functions.Tests.csproj` |

## Qué dispara cada uno

| App base | paths.include |
|---|---|
| `fa-axxoncontacts` | `src/integrations/contacts/AxxonContacts.Functions/**`<br>`src/core/**`<br>`pipelines/azure-pipelines-contacts.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |
| `fa-axxoncustomergroups` | `src/integrations/customers/AxxonCustomerGroups.Functions/**`<br>`src/core/**`<br>`pipelines/azure-pipelines-customergroups.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |
| `fa-axxoncustomers` | `src/integrations/customers/AxxonCustomers.Functions/**`<br>`src/core/**`<br>`tests/AxxonCustomers.Functions.Tests/**`<br>`pipelines/azure-pipelines-customers.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |
| `fa-axxonfiscal` | `src/integrations/fiscal/AxxonFiscal.Functions/**`<br>`src/core/**`<br>`pipelines/azure-pipelines-fiscal.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |
| `fa-axxonproducts` | `src/integrations/products/AxxonProducts.Functions/**`<br>`src/core/**`<br>`pipelines/azure-pipelines-products.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |
| `fa-axxonthinkchat` | `src/integrations/thinkchat/AxxonThinkchat.Functions/**`<br>`src/core/**`<br>`pipelines/azure-pipelines-thinkchat.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |
| `fa-axxonticketatencion` | `src/integrations/service/AxxonTicketAtencion.Functions/**`<br>`tests/AxxonTicketAtencion.Functions.Tests/**`<br>`src/core/**`<br>`pipelines/azure-pipelines-ticketatencion.yml`<br>`pipelines/templates/functionapp-build-deploy.yml`<br>`pipelines/templates/functionapp-deploy-stage.yml` |

## Infraestructura

| Pipeline | Disparo | paths.include |
|---|---|---|
| [azure-pipelines-infra-test.yml](../../../pipelines/azure-pipelines-infra-test.yml) | manual | — |
| [azure-pipelines-infra.yml](../../../pipelines/azure-pipelines-infra.yml) | automático | `infra/**`, `pipelines/azure-pipelines-infra.yml` |
