# AxxonCustomers.Functions

Azure Function (.NET 10 isolated) que sincroniza contacts de Dataverse hacia la entidad
**CustomersV3** de Finance & Operations cuando se califica un lead.

## Flujo

1. Un Service Endpoint de Dataverse publica el `RemoteExecutionContext` del mensaje
   **QualifyLead** en la cola **`leadcontacts`** del namespace de Service Bus
   **`dataverseinte`**.
2. `QualifyLeadCustomerSyncFunction` parsea el mensaje y extrae el Id del contact de
   `InputParameters -> OpportunityCustomerId` (solo cuando `LogicalName == "contact"`;
   si el customer es un account, el mensaje se completa sin procesar).
3. `ContactCustomerSyncService` recupera el contact (`contactid == Id`) con los campos
   del mapeo y resuelve los lookups relacionados (party number, grupo de clientes,
   moneda, terminos de pago, etc.).
4. `FoCustomerService` hace `POST {FoBaseUrl}/data/CustomersV3` con el payload mapeado.
   La compania destino viaja en `dataAreaId` (tomado de `msdyn_company.cdm_companycode`).
5. El `CustomerAccount` generado por F&O (number sequence) se escribe de vuelta en
   `msdyn_contactpersonid` del contact (write-back). Esto ademas da **idempotencia**:
   si el contact ya tiene `msdyn_contactpersonid`, el insert se omite.

## Mapeo (CustomersV3_Contact.json, direccion CRM -> AX)

| Contact (Dataverse)                                  | CustomersV3 (F&O)      | Nota                                         |
|------------------------------------------------------|------------------------|----------------------------------------------|
| `msdyn_company.cdm_companycode`                      | `dataAreaId`           | Requerido; sin compania el mensaje va a DLQ  |
| — (default)                                          | `PartyType`            | Siempre `"Person"`                           |
| `msdyn_partyid.msdyn_partynumber`                    | `PartyNumber`          | Vincula al party existente si lo hay         |
| `msdyn_contactpersonid`                              | `CustomerAccount`      | Omitido: lo genera F&O por number sequence   |
| `msdyn_customergroupid.msdyn_groupid`                | `CustomerGroupId`      |                                              |
| `msdyn_identificationnumber`                         | `IdentificationNumber` |                                              |
| `msdyn_partycountry`                                 | `PartyCountry`         |                                              |
| `msdyn_partystateprovince`                           | `PartyState`           |                                              |
| `transactioncurrencyid.isocurrencycode`              | `SalesCurrencyCode`    |                                              |
| `description`                                        | `SalesMemo`            |                                              |
| `msdyn_paymentday.msdyn_name`                        | `PaymentDay`           |                                              |
| `msdyn_paymentschedule.msdyn_name`                   | `PaymentSchedule`      |                                              |
| `msdyn_customerpaymentmethod.msdyn_name`             | `PaymentMethod`        |                                              |
| `msdyn_salestaxgroup.msdyn_name`                     | `SalesTaxGroup`        |                                              |
| `msdyn_paymentterms.msdyn_name`                      | `PaymentTerms`         |                                              |
| `msdyn_primarycontact.msdyn_contactforpartynumber`   | `ContactPersonId`      |                                              |
| `creditlimit`                                        | `CreditLimit`          |                                              |
| `a365_creditrating`                                  | `CreditRating`         | Texto u OptionSet (usa la etiqueta)          |
| `a365_onholdstatus`                                  | `OnHoldStatus`         | Value map invertido (806380000 -> "No", ...) |
| `a365_notes`                                         | `CredManNotes`         |                                              |
| `msdyn_sellable`                                     | `A365SELLABLE`         | true -> "Yes", false -> "No"                 |

## Manejo de errores (autoComplete = false)

| Situacion                                            | Accion                                  |
|------------------------------------------------------|-----------------------------------------|
| Body no parseable como RemoteExecutionContext        | DLQ (`ParseFailed`)                     |
| QualifyLead sin contact (customer = account o nulo)  | Complete sin procesar                   |
| Contact inexistente / sin `msdyn_company`            | DLQ (`DataError`)                       |
| Contact ya sincronizado (`msdyn_contactpersonid`)    | Complete sin re-insertar (idempotencia) |
| Error transitorio (F&O / Dataverse / red)            | Abandon -> retry de Service Bus -> DLQ tras Max Delivery Count |

## Application Settings

| Setting                 | Descripcion                                                       |
|-------------------------|-------------------------------------------------------------------|
| `ServiceBusConnection`  | Connection string (o config de identity) del namespace `dataverseinte` |
| `ServiceBusQueueName`   | `leadcontacts`                                                    |
| `DataverseUrl`          | URL del environment de Dataverse                                  |
| `DataverseClientId`     | (DESA) Client Id del app registration; vacio => Managed Identity  |
| `DataverseClientSecret` | (DESA) Secret del app registration                                |
| `FoBaseUrl`             | URL base del environment de F&O                                   |
| `FoTenantId`            | (DESA) Tenant para client-credentials contra F&O                  |
| `FoClientId`            | (DESA) Client Id; vacio => Managed Identity                       |
| `FoClientSecret`        | (DESA) Secret                                                     |

> En produccion usar Managed Identity de la Function App tanto para Dataverse
> (application user) como para F&O (registrar el client id en
> *System administration > Microsoft Entra applications*).
