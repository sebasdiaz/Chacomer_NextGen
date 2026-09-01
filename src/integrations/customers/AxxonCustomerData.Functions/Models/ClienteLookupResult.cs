using System.Text.Json.Serialization;

namespace AxxonCustomerData.Functions.Models
{
    /// <summary>
    /// Legal entity del cliente. Sale del lookup <c>msdyn_company</c> a
    /// <c>cdm_company</c>: el id y el nombre vienen en la EntityReference, y el
    /// <c>codigo</c> —el unico dato que sirve como clave del otro lado— de un link a la
    /// tabla, porque la EntityReference no lo trae.
    /// </summary>
    public sealed class LegalEntityInfo
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; }

        /// <summary>cdm_companycode. Es el <c>dataAreaId</c> con el que F&amp;O particiona.</summary>
        [JsonPropertyName("codigo")]
        public string? Codigo { get; init; }

        [JsonPropertyName("nombre")]
        public string? Nombre { get; init; }
    }

    /// <summary>
    /// Un cliente (contact o account) de Dataverse, con los datos que necesita un
    /// satelite para identificarlo y cruzarlo contra su propio maestro.
    ///
    /// Un mismo RUC devuelve normalmente varias filas: el master mas los raws que cuelgan
    /// de el, uno por legal entity — ver
    /// [Contacts](docs/wiki/integraciones/contacts.md). Por eso <c>esMaster</c> y
    /// <c>masterId</c> van en cada item: son lo que le permite al consumidor quedarse con
    /// la vista unificada o con la fila de una compania puntual.
    /// </summary>
    public sealed class ClienteLookupResult
    {
        [JsonPropertyName("id")]
        public Guid Id { get; init; }

        /// <summary>Logical name de la tabla: "contact" o "account".</summary>
        [JsonPropertyName("entidad")]
        public string Entidad { get; init; } = string.Empty;

        /// <summary>
        /// "Fisica" para contact, "Juridica" para account. Se deriva de la tabla, igual
        /// que en el endpoint de fiscal: el modelo no tiene un campo de personeria con
        /// esa semantica (<c>axx_tipopersoneriajuridica</c> es otra cosa — ver abajo).
        /// </summary>
        [JsonPropertyName("tipoPersona")]
        public string TipoPersona { get; init; } = string.Empty;

        /// <summary>fullname del contact o name del account.</summary>
        [JsonPropertyName("nombre")]
        public string? Nombre { get; init; }

        /// <summary>msdyn_identificationnumber, tal cual esta guardado ("80054203-7").</summary>
        [JsonPropertyName("identificationNumber")]
        public string? IdentificationNumber { get; init; }

        /// <summary>axx_ismaster: distingue el master de los raws con el mismo RUC.</summary>
        [JsonPropertyName("esMaster")]
        public bool EsMaster { get; init; }

        /// <summary>
        /// Master del que cuelga este raw (<c>axx_mastercontactid</c> /
        /// <c>axx_masteraccountid</c>). Null en el master y en los raws que todavia no
        /// matchearon.
        /// </summary>
        [JsonPropertyName("masterId")]
        public Guid? MasterId { get; init; }

        /// <summary>
        /// Numero de cliente en F&amp;O: el write-back de
        /// [Customers](docs/wiki/integraciones/customers.md)
        /// (<c>msdyn_contactpersonid</c> en contact, <c>accountnumber</c> en account).
        ///
        /// <b>Vacio no significa que el cliente no exista en el ERP</b>, solo que este
        /// registro no tiene el write-back: los masters no se sincronizan a F&amp;O, y un
        /// raw recien creado puede estar todavia en la cola.
        /// </summary>
        [JsonPropertyName("customerAccount")]
        public string? CustomerAccount { get; init; }

        /// <summary>Legal entity del registro. Null en los masters, que no tienen compania.</summary>
        [JsonPropertyName("legalEntity")]
        public LegalEntityInfo? LegalEntity { get; init; }

        /// <summary>
        /// Etiqueta de <c>axx_tipopersoneriajuridica</c> (OptionSet). Es el tipo de
        /// personeria del maestro de Chacomer, no la derivacion contact/account de
        /// <see cref="TipoPersona"/>.
        /// </summary>
        [JsonPropertyName("tipoPersoneriaJuridica")]
        public string? TipoPersoneriaJuridica { get; init; }

        /// <summary>Nombre de la fila de <c>axx_tipodocumento</c> (el lookup de tipo de documento).</summary>
        [JsonPropertyName("tipoDocumento")]
        public string? TipoDocumento { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("telefono")]
        public string? Telefono { get; init; }

        /// <summary>statecode = 0. Un cliente desactivado sigue apareciendo en la consulta.</summary>
        [JsonPropertyName("activo")]
        public bool Activo { get; init; }
    }

    /// <summary>Respuesta del endpoint: el RUC consultado y los clientes que matchearon.</summary>
    public sealed class ClienteLookupResponse
    {
        [JsonPropertyName("ruc")]
        public string Ruc { get; init; } = string.Empty;

        [JsonPropertyName("cantidad")]
        public int Cantidad => Clientes.Count;

        [JsonPropertyName("clientes")]
        public IReadOnlyList<ClienteLookupResult> Clientes { get; init; } = [];
    }
}
