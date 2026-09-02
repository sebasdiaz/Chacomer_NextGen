<!-- wiki-meta
sources:
  - Chacomer.sln
sources_new:
  - src/**/*.csproj
  - src/**/*.pcfproj
  - tests/**/*.csproj
last_reviewed: 2026-09-02
-->

# Visión general de la EiP

La **Enterprise Integration Platform (EiP)** de Chacomer conecta Dynamics 365 (Dataverse),
Finance & Operations y los satélites externos a través de un backbone asincrónico de Azure
Service Bus, con la lógica de cada integración en Azure Functions .NET aisladas.

| Para saber | Ir a |
|---|---|
| Qué contrato hablan los sistemas entre sí | [Mensajería y contratos](mensajeria.md) |
| Qué comparten todas las Function Apps | [Axxon.Eip.Core](../plataforma/eip-core.md) |
| Dónde corre todo y cómo se despliega | [Infraestructura](../plataforma/infraestructura.md) · [Ambientes](../plataforma/ambientes.md) · [Pipelines](../plataforma/pipelines.md) |
| Qué hace cada integración | [Integraciones](../integraciones.md) |
| Por qué está hecho así | [Decisiones](decisiones.md) |

## Estructura del solution

```
Chacomer_NextGen/
├── Chacomer.sln
├── generate-snk.ps1                   (Strong Name Key del plugin)
│
├── docs/
│   ├── wiki/                          (esta wiki)
│   └── contracts/                     (contratos de mensajes de la EiP — JSON Schema)
│
├── infra/                             (Bicep — ver Plataforma › Infraestructura)
│   ├── main.bicep
│   ├── modules/                       (monitoring, keyvault, servicebus, functionApp)
│   ├── environments/                  (inte, test, uat, prod .bicepparam)
│   └── scripts/
│
├── pipelines/                         (Azure Pipelines — uno por integración + infra)
│   └── templates/
│
├── tests/
│   ├── AxxonContacts.Functions.Tests/
│   └── AxxonCustomers.Functions.Tests/    (xUnit — mapeos CRM -> F&O)
│
└── src/
    ├── core/
    │   └── Axxon.Eip.Core/            (.NET 10 — componentes CROSS de la EiP)
    │       ├── Configuration/         (DataverseOptions, FoODataOptions, Key Vault)
    │       ├── Dataverse/             (DataverseClientFactory + AddEipDataverse)
    │       ├── FinOps/                (FoODataClient generico + AddEipFoOData + retry 429)
    │       ├── Fiscal/                (clientes SET / TURUC)
    │       ├── Hosting/               (AddEipCore: Key Vault + OpenTelemetry + logging)
    │       └── Messaging/             (EipMessage, EipConstants, EipServiceBusPublisher)
    │
    ├── integrations/
    │   ├── contacts/
    │   │   ├── AxxonContacts.Plugins/     (.NET 4.6.2 — plugin Dataverse)
    │   │   ├── AxxonContacts.Functions/   (.NET 10 — master matching contact/account)
    │   │   ├── AxxonContacts.PCF/         (RucValidatorControl)
    │   │   └── AxxonContacts.WebResources/
    │   ├── customers/
    │   │   ├── AxxonCustomers.Functions/       (QualifyLead + fo-sync -> CustomersV3)
    │   │   ├── AxxonCustomerData.Functions/    (HTTP: consulta de clientes por RUC)
    │   │   └── AxxonCustomerGroups.Functions/  (timer F&O -> Dataverse)
    │   ├── fiscal/
    │   │   └── AxxonFiscal.Functions/     (proxy HTTP SET / TURUC)
    │   ├── products/
    │   │   └── AxxonProducts.Functions/   (timer F&O -> Dataverse)
    │   └── thinkchat/
    │       └── AxxonThinkchat.Functions/  (timer Thinkchat -> Dataverse)
    │
    └── webresources/                  (PCF controls — ver Web resources)
        ├── DeviceRegistrationGrid/
        ├── DnitResponseViewer/
        ├── MasterAccountChildrenGrid/
        ├── MasterContactAccountGrid/
        ├── MasterContactAddressesGrid/
        └── MasterContactChildrenGrid/
```

## Las Function Apps

Una app por dominio, todas .NET 10 isolated sobre Flex Consumption. El detalle de cada
una está en su página de [integración](../integraciones.md).

> El título no lleva el número a propósito: venía diciendo "seis" con siete apps en la
> tabla. La cuenta viva está en el [inventario generado](../_generado/funciones.md).

| Function App | Integración | Disparador |
|---|---|---|
| `fa-axxoncontacts-{env}` | [Contacts](../integraciones/contacts.md) | Service Bus — `contact-master-matching`, `account-master-matching` |
| `fa-axxoncustomers-{env}` | [Customers](../integraciones/customers.md) | Service Bus — `leadcontacts`, `customer-fo-sync` |
| `fa-axxoncustomercredit-{env}` | [Customer credit](../integraciones/customercredit.md) | HTTP — créditos de clientes desde F&O para satélites |
| `fa-axxoncustomerdata-{env}` | [Customer data](../integraciones/customerdata.md) | HTTP — consulta por RUC de un satélite externo |
| `fa-axxoncustomergroups-{env}` | [Customer groups](../integraciones/customergroups.md) | Timer |
| `fa-axxonproducts-{env}` | [Products](../integraciones/products.md) | Timer |
| `fa-axxonfiscal-{env}` | [Fiscal](../integraciones/fiscal.md) | HTTP |
| `fa-axxonthinkchat-{env}` | [Thinkchat](../integraciones/thinkchat.md) | Timer + HTTP |
| `fa-axxonticketatencion-{env}` | [Ticket de Atención](../integraciones/ticketatencion.md) | HTTP — botón en el formulario de Cita de Servicio |

> El nombre real de las apps de INTE no siempre sigue el patrón (ej. `fa-axxoncustomergroup`,
> sin `s`). Ver [Ambientes](../plataforma/ambientes.md).
