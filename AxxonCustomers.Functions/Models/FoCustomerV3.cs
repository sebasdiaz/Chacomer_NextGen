using System.Text.Json.Serialization;

namespace AxxonCustomers.Functions.Models
{
    /// <summary>
    /// DTO para el POST a la entidad CustomersV3 de F&O.
    /// Campos segun CustomersV3_Contact.json (mapeo dual-write [Customers V3] - [contacts])
    /// invertido para la direccion CRM -> AX:
    ///
    ///   dataAreaId            &lt;- msdyn_company.cdm_companycode
    ///   PartyType             &lt;- default "Person"
    ///   PartyNumber           &lt;- msdyn_partyid.msdyn_partynumber
    ///   CustomerAccount       &lt;- msdyn_contactpersonid (omitido si vacio: F&O genera por number sequence)
    ///   CustomerGroupId       &lt;- msdyn_customergroupid.msdyn_groupid
    ///   IdentificationNumber  &lt;- msdyn_identificationnumber
    ///   PartyCountry          &lt;- msdyn_partycountry
    ///   PartyState            &lt;- msdyn_partystateprovince
    ///   SalesCurrencyCode     &lt;- transactioncurrencyid.isocurrencycode
    ///   SalesMemo             &lt;- description
    ///   PaymentDay            &lt;- msdyn_paymentday.msdyn_name
    ///   PaymentSchedule       &lt;- msdyn_paymentschedule.msdyn_name
    ///   PaymentMethod         &lt;- msdyn_customerpaymentmethod.msdyn_name
    ///   SalesTaxGroup         &lt;- msdyn_salestaxgroup.msdyn_name
    ///   PaymentTerms          &lt;- msdyn_paymentterms.msdyn_name
    ///   ContactPersonId       &lt;- msdyn_primarycontact.msdyn_contactforpartynumber
    ///   CreditLimit           &lt;- creditlimit
    ///   CreditRating          &lt;- a365_creditrating
    ///   OnHoldStatus          &lt;- a365_onholdstatus (value map invertido)
    ///   CredManNotes          &lt;- a365_notes
    ///   A365Sellable          &lt;- msdyn_sellable (true -> "Yes", false -> "No")
    ///
    /// Los nulls se omiten en la serializacion (JsonIgnoreCondition.WhenWritingNull)
    /// para que F&O aplique sus defaults.
    /// </summary>
    public class FoCustomerV3
    {
        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("PartyType")]
        public string PartyType { get; set; } = "Person";

        [JsonPropertyName("PartyNumber")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PartyNumber { get; set; }

        [JsonPropertyName("CustomerAccount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CustomerAccount { get; set; }

        [JsonPropertyName("CustomerGroupId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CustomerGroupId { get; set; }

        [JsonPropertyName("IdentificationNumber")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? IdentificationNumber { get; set; }

        [JsonPropertyName("PartyCountry")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PartyCountry { get; set; }

        [JsonPropertyName("PartyState")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PartyState { get; set; }

        [JsonPropertyName("SalesCurrencyCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SalesCurrencyCode { get; set; }

        [JsonPropertyName("SalesMemo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SalesMemo { get; set; }

        [JsonPropertyName("PaymentDay")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PaymentDay { get; set; }

        [JsonPropertyName("PaymentSchedule")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PaymentSchedule { get; set; }

        [JsonPropertyName("PaymentMethod")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PaymentMethod { get; set; }

        [JsonPropertyName("SalesTaxGroup")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SalesTaxGroup { get; set; }

        [JsonPropertyName("PaymentTerms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PaymentTerms { get; set; }

        [JsonPropertyName("ContactPersonId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContactPersonId { get; set; }

        [JsonPropertyName("CreditLimit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? CreditLimit { get; set; }

        [JsonPropertyName("CreditRating")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CreditRating { get; set; }

        [JsonPropertyName("OnHoldStatus")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OnHoldStatus { get; set; }

        [JsonPropertyName("CredManNotes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CredManNotes { get; set; }

        [JsonPropertyName("A365SELLABLE")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? A365Sellable { get; set; }
    }

    /// <summary>
    /// Respuesta de F&O al crear el customer. Solo interesa el CustomerAccount
    /// generado (number sequence) para el write-back al contact.
    /// </summary>
    public class FoCustomerV3CreatedResponse
    {
        [JsonPropertyName("CustomerAccount")]
        public string? CustomerAccount { get; set; }

        [JsonPropertyName("PartyNumber")]
        public string? PartyNumber { get; set; }

        [JsonPropertyName("dataAreaId")]
        public string? DataAreaId { get; set; }
    }
}
