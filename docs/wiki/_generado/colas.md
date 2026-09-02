<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Colas del Service Bus

> Generado desde el código por [`generate.mjs`](../generate.mjs). Si algo de acá está
> mal, el que está mal es el código — o el generador. No edites esta página.

Lo que declara `infra/modules/servicebus.bicep` para `sb-chacomer-eip-{env}`, cruzado con
las funciones que las consumen. El **porqué** de cada decisión (qué colas llevan sessions, y
por qué sólo la que alimentan terceros lleva detección de duplicados) está en
[Infraestructura › Queues del namespace](../plataforma/infraestructura.md#queues-del-namespace).

| Cola | Sessions | Dedup | Consumidor |
|---|---|---|---|
| `contact-master-matching` | no | no | `ContactMasterMatchingFunction` |
| `account-master-matching` | no | no | `AccountMasterMatchingFunction` |
| `customer-fo-sync` | si | no | `CustomerFoSyncFunction` |
| `customer-ltm-sync` | si | no | `LtmCustSyncFunction` |
| `leadcontacts` | no | no | `QualifyLeadCustomerSyncFunction` |
| `lead-intake` | no | si | `LeadIntakeFunction` |

### Propiedades, iguales para todas

| Propiedad | Valor |
|---|---|
| `lockDuration` | `PT5M` |
| `maxDeliveryCount` | `3` |
| `defaultMessageTimeToLive` | `P1D` |
| `deadLetteringOnMessageExpiration` | `true` |
