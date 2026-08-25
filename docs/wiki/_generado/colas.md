<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Colas del Service Bus

> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está
> mal, el que está mal es el código — o el generador. No edites esta página.

Lo que declara `infra/modules/servicebus.bicep` para `sb-chacomer-eip-{env}`, cruzado con
las funciones que las consumen. El **porqué** de cada decisión (sobre todo por qué sólo una
lleva sessions) está en
[Infraestructura › Queues del namespace](../plataforma/infraestructura.md#queues-del-namespace).

| Cola | Sessions | Consumidor |
|---|---|---|
| `contact-master-matching` | no | `ContactMasterMatchingFunction` |
| `account-master-matching` | no | `AccountMasterMatchingFunction` |
| `customer-fo-sync` | si | `CustomerFoSyncFunction` |
| `customer-ltm-sync` | si | `LtmCustSyncFunction` |
| `leadcontacts` | no | `QualifyLeadCustomerSyncFunction` |

### Propiedades, iguales para todas

| Propiedad | Valor |
|---|---|
| `lockDuration` | `PT5M` |
| `maxDeliveryCount` | `3` |
| `defaultMessageTimeToLive` | `P1D` |
| `deadLetteringOnMessageExpiration` | `true` |
