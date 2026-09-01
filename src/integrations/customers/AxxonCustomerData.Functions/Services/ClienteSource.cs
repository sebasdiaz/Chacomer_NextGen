namespace AxxonCustomerData.Functions.Services
{
    /// <summary>
    /// Lo unico que difiere entre <c>contact</c> y <c>account</c> al leer un cliente: el
    /// logical name y los cuatro atributos que no se llaman igual en las dos tablas. El
    /// resto de las columnas son las mismas, asi que la consulta se escribe una sola vez.
    ///
    /// Mismo criterio (y misma forma) que <c>LtmCustSource</c> en AxxonCustomers: los
    /// nombres fisicos juntos en un lugar, no repartidos por el servicio.
    /// </summary>
    public sealed record ClienteSource(
        string EntityLogicalName,
        string NameAttribute,
        string CustomerAccountAttribute,
        string MasterAttribute,
        string TipoDocumentoAttribute,
        string TipoPersona)
    {
        /// <summary>
        /// contact: el write-back de F&amp;O va a <c>msdyn_contactpersonid</c> y el master
        /// a <c>axx_mastercontactid</c>.
        /// </summary>
        public static readonly ClienteSource Contact =
            new("contact", "fullname", "msdyn_contactpersonid", "axx_mastercontactid",
                "axx_tipodocumento", "Fisica");

        /// <summary>
        /// account: el write-back va al <c>accountnumber</c> OOB y el master a
        /// <c>axx_masteraccountid</c>.
        ///
        /// <b>El tipo de documento se llama distinto que en contact</b>: aca es
        /// <c>axx_tipodedocumento</c>, con el "de" en el medio. Verificado contra la
        /// metadata de INTE y de TEST — no es un typo de un ambiente, los dos lo tienen
        /// asi. Pedirle a account el nombre de contact hace que el RetrieveMultiple tire, y
        /// como accounts se consulta primero, se cae la respuesta entera.
        /// </summary>
        public static readonly ClienteSource Account =
            new("account", "name", "accountnumber", "axx_masteraccountid",
                "axx_tipodedocumento", "Juridica");

        /// <summary>Columnas que pide la consulta, con las comunes a las dos tablas.</summary>
        public string[] Columns =>
        [
            NameAttribute,
            CustomerAccountAttribute,
            MasterAttribute,
            TipoDocumentoAttribute,
            ClienteAttributes.IdentificationNumber,
            ClienteAttributes.IsMaster,
            ClienteAttributes.Company,
            ClienteAttributes.TipoPersoneria,
            ClienteAttributes.Email,
            ClienteAttributes.Telefono,
            ClienteAttributes.StateCode
        ];
    }

    /// <summary>
    /// Atributos que se llaman igual en contact y en account. Los nombres son los mismos
    /// que usan contacts (master matching) y customers (mapeo a F&amp;O): si alguno cambia
    /// en Dataverse, cambia en los tres lados.
    /// </summary>
    public static class ClienteAttributes
    {
        public const string IdentificationNumber = "msdyn_identificationnumber";
        public const string IsMaster             = "axx_ismaster";
        public const string Company              = "msdyn_company";

        /// <summary>
        /// Lookup a la tabla de personerias. Se lee por etiqueta igual que un OptionSet:
        /// en un lookup, FormattedValues devuelve el nombre primario de la fila apuntada.
        /// </summary>
        public const string TipoPersoneria       = "axx_tipopersoneriajuridica";

        // El tipo de documento NO esta aca: se llama distinto en cada tabla
        // (axx_tipodocumento / axx_tipodedocumento). Vive en ClienteSource.

        public const string Email                = "emailaddress1";
        public const string Telefono             = "telephone1";
        public const string StateCode            = "statecode";

        // ── cdm_company (la legal entity) ─────────────────────────────

        public const string CompanyEntity   = "cdm_company";
        public const string CompanyIdKey    = "cdm_companyid";
        public const string CompanyCode     = "cdm_companycode";

        /// <summary>Alias del link a <c>cdm_company</c>, con el que vuelve el codigo aliaseado.</summary>
        public const string CompanyAlias    = "le";
    }
}
