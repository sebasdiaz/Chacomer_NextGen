namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Nombres fisicos de todo lo que participa del mapeo hacia <c>LTMCustTable</c>, y las
    /// dos constantes que fija el alcance funcional de la v1 (RUC y Paraguay).
    ///
    /// Estan juntos aca, y no dispersos por el builder, porque son la parte del mapeo que
    /// no se puede verificar leyendo el repo. <b>Todos se verificaron contra la metadata de
    /// INTE, y los de Dataverse tambien contra TEST (2026-09-03).</b> La version anterior los
    /// nombraba siguiendo la convencion del proveedor y cuatro no existian: el lookup de tipo
    /// de documento, su equivalente en account, y los dos de la direccion. Ver ADR-001.
    /// </summary>
    public static class LtmCustMapping
    {
        /// <summary>Entity set de la OData API de F&amp;O.</summary>
        public const string EntitySet = "LTMCustTables";

        // ── Campos de LTMCustTable (destino) ──────────────────────────
        //
        // Los nueve existen en el $metadata de F&O con este casing exacto, y la clave de la
        // tabla es (dataAreaId, AccountNum). Igual pasan por IFoSchemaProvider, que los
        // confirma contra el entity set del ambiente antes de mandarlos.

        public const string DataAreaId         = "dataAreaId";
        public const string AccountNum         = "AccountNum";
        public const string CountryDocTypeId   = "CountryDocTypeId";
        public const string CountryDocNum      = "CountryDocNum";
        public const string StateDocNum        = "StateDocNum";
        public const string TaxPayerTypeId     = "TaxPayerTypeId";
        public const string AccountTypeGroupId = "AccountTypeGroupId";
        public const string CountryRegionId    = "CountryRegionId";
        public const string StateId            = "StateId";

        // ── El alcance funcional de la v1: RUC y Paraguay ─────────────

        /// <summary>
        /// Tipo de documento del cliente. Es una constante a proposito: el alcance funcional
        /// definido hoy es RUC.
        ///
        /// <b>Esto es lo que reemplaza al lookup del analisis funcional.</b> Aquel asumia que
        /// el cliente apuntaba con un lookup a <c>mserp_ltmtaxpayerdoctypeentity</c>; en la
        /// realidad esa virtual entity no tiene ninguna relacion 1:N, y lo que hay en el
        /// cliente es un OptionSet local (CI / RUC / Passport) que ademas se llama distinto
        /// en contact (<c>axx_tipodocumento</c>) que en account (<c>axx_tipodedocumento</c>).
        /// Cuando se amplie el alcance a CI y pasaporte hay que volver a leer ese OptionSet y
        /// traducirlo a los codigos del ERP (CI -> CedID, Passport -> PSP).
        /// </summary>
        public const string CountryDocTypeRuc = "RUC";

        /// <summary>
        /// Pais del cliente, constante por el mismo motivo: el alcance es Paraguay.
        /// <c>PRY</c> es el codigo de F&amp;O, no el <c>PY</c> que guarda Dataverse en
        /// <c>customeraddress.country</c> — copiar el campo de CRM tal cual escribiria un
        /// codigo que el ERP no conoce.
        /// </summary>
        public const string CountryRegionParaguay = "PRY";

        // ── Dataverse: registro principal (contact / account) ─────────

        /// <summary>Lookup a <c>cdm_company</c>. Mismo atributo que usa el mapeo de CustomersV3.</summary>
        public const string CompanyAttribute = "msdyn_company";

        /// <summary>Codigo de la legal entity dentro de <c>cdm_company</c>.</summary>
        public const string CompanyCodeAttribute = "cdm_companycode";

        /// <summary>RUC del cliente. Alimenta <c>CountryDocNum</c> y <c>StateDocNum</c>.</summary>
        public const string IdentificationNumberAttribute = "msdyn_identificationnumber";

        // ── Dataverse: mserp_ltmtaxpayerdoctypeentity (virtual) ───────
        //
        // Ya no se navega desde el cliente: se consulta por company para saber si la legal
        // entity tiene la localizacion PY configurada, y para confirmar que el tipo de
        // contribuyente que le corresponde al registro existe en el ERP.

        public const string VirtualDocTypeEntity  = "mserp_ltmtaxpayerdoctypeentity";
        public const string VirtualDocTypeId      = "mserp_doctypeid";
        public const string VirtualTaxPayerTypeId = "mserp_taxpayertypeid";
        public const string VirtualDocTypeCompany = "mserp_dataareaid";

        // ── Dataverse: mserp_ltmaccounttypegroupentity (virtual) ──────

        public const string VirtualAccountTypeGroupEntity   = "mserp_ltmaccounttypegroupentity";
        public const string VirtualAccountTypeGroupId       = "mserp_accounttypegroupid";
        public const string VirtualAccountTypeGroupCompany  = "mserp_dataareaid";
        public const string VirtualAccountTypeGroupCustVend = "mserp_custvendentity";

        /// <summary>
        /// Valor <c>Customer</c> del OptionSet <c>mserp_custvendentity</c> (los otros son
        /// 200000001 Vendor y 200000002 None).
        ///
        /// <b>Es un Picklist, no un string.</b> Filtrarlo con la etiqueta —como hacia la
        /// version anterior— no devuelve vacio: la query tira
        /// <c>System.FormatException ... Expected type of attribute value: System.Int32</c>,
        /// asi que el mensaje terminaba en el DLQ sin llegar nunca a F&amp;O.
        /// </summary>
        public const int CustVendEntityCustomer = 200000000;

        // ── Dataverse: customeraddress ────────────────────────────────

        public const string AddressEntity = "customeraddress";

        /// <summary>Lookup de la direccion al registro duenio (contact o account).</summary>
        public const string AddressParentAttribute = "parentid";

        /// <summary>
        /// Numero de direccion. Ya no se filtra por el 1: se usa para ordenar y quedarse con
        /// la mas vieja de las que tienen dato. Dataverse crea automaticamente las direcciones
        /// 1 y 2 de cada cliente y en este environment casi nunca se completan —las cargadas
        /// a mano arrancan en la 3—, asi que filtrar por <c>addressnumber = 1</c> apuntaba
        /// justo a la fila vacia.
        /// </summary>
        public const string AddressNumberAttribute = "addressnumber";

        /// <summary>
        /// Estado/departamento de la direccion. Es el campo OOB: <c>customeraddress</c> no
        /// tiene ningun lookup custom, asi que las cadenas <c>axx_pais</c> / <c>axx_region</c>
        /// del analisis funcional no existen. Las tablas <c>axx_pais</c> y <c>axx_region</c>
        /// si existen, pero cuelgan de otro arbol (pais &lt;- region &lt;- localidad &lt;- barrio)
        /// y nada las conecta con la direccion del cliente.
        /// </summary>
        public const string AddressStateAttribute = "stateorprovince";

        // ── F&O: catalogo de estados ──────────────────────────────────

        /// <summary>Entity set de F&amp;O con los estados/departamentos por pais.</summary>
        public const string FoStateEntitySet = "AddressStates";

        /// <summary>Filtro por pais dentro de <see cref="FoStateEntitySet"/>.</summary>
        public const string FoStateCountryRegionField = "CountryRegionId";

        /// <summary>
        /// Codigo del estado dentro de <see cref="FoStateEntitySet"/>. Se llama <c>State</c>,
        /// no <c>StateId</c> — que es como se llama el campo destino en <c>LTMCustTable</c>.
        /// </summary>
        public const string FoStateField = "State";
    }

    /// <summary>
    /// Lo que difiere entre <c>contact</c> y <c>account</c>: el logical name, el atributo
    /// donde <see cref="Services.CustomerSyncService"/> deja el <c>CustomerAccount</c> que
    /// genero F&amp;O, y el tipo de contribuyente que le corresponde.
    /// </summary>
    public sealed record LtmCustSource(
        string EntityLogicalName,
        string AccountNumberAttribute,
        string TaxPayerTypeId)
    {
        /// <summary>
        /// contact: write-back en <c>msdyn_contactpersonid</c>, y persona fisica (<c>PN</c>).
        ///
        /// El tipo de contribuyente sale del tipo de registro y no de
        /// <c>axx_tipopersoneriajuridica</c>, que seria la fuente mas fiel al dato pero esta
        /// vacia en la mayoria de los clientes (43 de 142 contacts y 3 de 142 accounts en
        /// INTE). Es la misma decision que ya tomaron los overlays de CustomersV3, que sellan
        /// <c>PartyType</c> constante e inmutable: Person para contact, Organization para
        /// account.
        /// </summary>
        public static readonly LtmCustSource Contact = new("contact", "msdyn_contactpersonid", "PN");

        /// <summary>account: write-back en el <c>accountnumber</c> OOB, y persona juridica (<c>PJ</c>).</summary>
        public static readonly LtmCustSource Account = new("account", "accountnumber", "PJ");

        /// <summary>Resuelve la fuente por logical name, o null si no es una de las dos.</summary>
        public static LtmCustSource? For(string? entityLogicalName) => entityLogicalName?.ToLowerInvariant() switch
        {
            "contact" => Contact,
            "account" => Account,
            _         => null
        };

        /// <summary>
        /// Columnas del registro principal que necesita el mapeo. Son las mismas para las dos
        /// entidades: con el tipo de documento constante, el unico atributo que se llamaba
        /// distinto en cada una salio del mapeo.
        /// </summary>
        public string[] Columns =>
        [
            LtmCustMapping.CompanyAttribute,
            LtmCustMapping.IdentificationNumberAttribute,
            AccountNumberAttribute
        ];
    }
}
