namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Nombres fisicos de todo lo que participa del mapeo hacia <c>LTMCustTable</c>.
    ///
    /// Estan juntos aca, y no dispersos por el builder, porque son la parte del mapeo que
    /// <b>no se puede verificar leyendo el repo</b>: los campos de las virtual entities
    /// (<c>mserp_*</c>) los publica el proveedor de F&amp;O en cada environment, y tanto el
    /// prefijo como el casing hay que confirmarlos contra la metadata del ambiente. Cuando
    /// alguno no coincida, se corrige aca y en ningun otro lado.
    ///
    /// Los nombres de los campos de F&amp;O salen del registro real de LTMCustTable que
    /// paso el funcional, asi que el casing ya viene del ERP; igual pasan por
    /// <see cref="IFoSchemaProvider"/>, que los confirma contra el entity set.
    /// </summary>
    public static class LtmCustMapping
    {
        /// <summary>Entity set de la OData API de F&amp;O.</summary>
        public const string EntitySet = "LTMCustTables";

        // ── Campos de LTMCustTable (destino) ──────────────────────────

        public const string DataAreaId         = "dataAreaId";
        public const string AccountNum         = "AccountNum";
        public const string CountryDocTypeId   = "CountryDocTypeId";
        public const string CountryDocNum      = "CountryDocNum";
        public const string StateDocNum        = "StateDocNum";
        public const string TaxPayerTypeId     = "TaxPayerTypeId";
        public const string AccountTypeGroupId = "AccountTypeGroupId";
        public const string CountryRegionId    = "CountryRegionId";
        public const string StateId            = "StateId";

        // ── Dataverse: registro principal (contact / account) ─────────

        /// <summary>Lookup a <c>cdm_company</c>. Mismo atributo que usa el mapeo de CustomersV3.</summary>
        public const string CompanyAttribute = "msdyn_company";

        /// <summary>Codigo de la legal entity dentro de <c>cdm_company</c>.</summary>
        public const string CompanyCodeAttribute = "cdm_companycode";

        /// <summary>RUC del cliente. Alimenta <c>CountryDocNum</c> y <c>StateDocNum</c>.</summary>
        public const string IdentificationNumberAttribute = "msdyn_identificationnumber";

        /// <summary>
        /// Lookup del registro principal <b>directo</b> a la virtual entity de tipos de
        /// documento. De esa unica fila salen los dos codigos: <c>CountryDocTypeId</c> y
        /// <c>TaxPayerTypeId</c>.
        /// </summary>
        public const string DocTypeAttribute = "axx_tipodocumento";

        // ── Dataverse: mserp_ltmtaxpayerdoctypeentity (virtual) ───────

        public const string VirtualDocTypeEntity   = "mserp_ltmtaxpayerdoctypeentity";
        public const string VirtualDocTypeId       = "mserp_doctypeid";
        public const string VirtualTaxPayerTypeId  = "mserp_taxpayertypeid";

        // ── Dataverse: mserp_ltmaccounttypegroupentity (virtual) ──────

        public const string VirtualAccountTypeGroupEntity   = "mserp_ltmaccounttypegroupentity";
        public const string VirtualAccountTypeGroupId       = "mserp_accounttypegroupid";
        public const string VirtualAccountTypeGroupCompany  = "mserp_dataareaid";
        public const string VirtualAccountTypeGroupCustVend = "mserp_custvendentity";

        /// <summary>
        /// Filtro fijo de la busqueda del grupo: el mapeo funcional pide la fila de
        /// <c>CustVendEntity = "Customer"</c> de la legal entity. Es lo unico del mapeo que
        /// no se navega sino que se consulta.
        /// </summary>
        public const string CustVendEntityCustomer = "Customer";

        // ── Dataverse: customeraddress ────────────────────────────────

        public const string AddressEntity = "customeraddress";

        /// <summary>Lookup de la direccion al registro duenio (contact o account).</summary>
        public const string AddressParentAttribute = "parentid";

        /// <summary>
        /// Numero de direccion. La primaria es la 1: es la que el formulario muestra como
        /// "Address 1" y la que el funcional definio como fuente de pais y region.
        /// </summary>
        public const string AddressNumberAttribute = "addressnumber";
        public const int    PrimaryAddressNumber   = 1;

        /// <summary>Lookup de la direccion a la tabla custom de paises.</summary>
        public const string AddressCountryLookup = "axx_pais";

        /// <summary>Codigo de pais dentro de <c>axx_pais</c>. Alimenta <c>CountryRegionId</c>.</summary>
        public const string CountryCodeAttribute = "axx_countryregion";

        /// <summary>Lookup de la direccion a la tabla custom de regiones.</summary>
        public const string AddressStateLookup = "axx_region";

        /// <summary>Nombre de la region dentro de <c>axx_region</c>. Alimenta <c>StateId</c>.</summary>
        public const string StateNameAttribute = "axx_name";
    }

    /// <summary>
    /// Lo unico que difiere entre <c>contact</c> y <c>account</c>: el logical name y el
    /// atributo donde <see cref="Services.CustomerSyncService"/> deja el
    /// <c>CustomerAccount</c> que genero F&amp;O. Las cadenas de navegacion del mapeo son
    /// las mismas para los dos.
    /// </summary>
    public sealed record LtmCustSource(string EntityLogicalName, string AccountNumberAttribute)
    {
        /// <summary>contact: el write-back va a <c>msdyn_contactpersonid</c>.</summary>
        public static readonly LtmCustSource Contact = new("contact", "msdyn_contactpersonid");

        /// <summary>account: el write-back va al <c>accountnumber</c> OOB.</summary>
        public static readonly LtmCustSource Account = new("account", "accountnumber");

        /// <summary>Resuelve la fuente por logical name, o null si no es una de las dos.</summary>
        public static LtmCustSource? For(string? entityLogicalName) => entityLogicalName?.ToLowerInvariant() switch
        {
            "contact" => Contact,
            "account" => Account,
            _         => null
        };

        /// <summary>Columnas del registro principal que necesita el mapeo.</summary>
        public string[] Columns =>
        [
            LtmCustMapping.CompanyAttribute,
            LtmCustMapping.IdentificationNumberAttribute,
            LtmCustMapping.DocTypeAttribute,
            AccountNumberAttribute
        ];
    }
}
