using System.Text.Json.Serialization;

namespace AxxonLeads.Functions.Models
{
    /// <summary>
    /// Payload de la cola de alta de leads (<c>lead-intake</c>): lo que manda un satelite
    /// —Thinkchat, el sitio web, un formulario de campana— para que se cree un lead en
    /// Dataverse.
    ///
    /// A diferencia de <c>CustomerSyncPayload</c>, este payload SI es un snapshot y no una
    /// referencia: el registro todavia no existe en ningun lado, asi que el mensaje es la
    /// unica fuente del dato. Esa es tambien la razon por la que el contrato es explicito
    /// campo por campo y no un diccionario libre — el satelite no conoce (ni deberia
    /// conocer) los logical names de Dataverse, y un typo en una clave suelta se
    /// descubriria recien al escribir.
    ///
    /// De donde viene el mensaje lo dice el envelope (<c>source</c>); que operacion es, el
    /// <c>operation</c>. Nada de eso se repite aca para no tener dos fuentes de verdad.
    /// </summary>
    public sealed class LeadIntakePayload
    {
        /// <summary>
        /// Id del lead en el sistema origen. Es la clave de idempotencia: si el app setting
        /// <c>LeadExternalIdAttribute</c> declara en que columna guardarlo, el consumidor
        /// busca por ahi antes de crear y no duplica ante una reentrega.
        /// Opcional — sin el, la unica proteccion es la deteccion de duplicados de la cola.
        /// </summary>
        [JsonPropertyName("externalId")]
        public string? ExternalId { get; set; }

        /// <summary>Tema del lead (<c>subject</c>). Obligatorio.</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        /// <summary>Nombre de la persona (<c>firstname</c>).</summary>
        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        /// <summary>
        /// Apellido (<c>lastname</c>). Obligatorio salvo que venga
        /// <see cref="CompanyName"/>: Dataverse pide identificar al lead por persona o por
        /// empresa, y un lead sin ninguno de los dos es una fila sin nombre.
        /// </summary>
        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        /// <summary>Razon social (<c>companyname</c>). Ver <see cref="LastName"/>.</summary>
        [JsonPropertyName("companyName")]
        public string? CompanyName { get; set; }

        /// <summary>
        /// RUC o cedula. Obligatorio: es la clave con la que despues se cruza el lead
        /// contra el master de contacts/accounts. El logical name de la columna destino
        /// sale del app setting <c>LeadIdentificationAttribute</c>.
        /// </summary>
        [JsonPropertyName("identificationNumber")]
        public string? IdentificationNumber { get; set; }

        /// <summary>Cargo (<c>jobtitle</c>).</summary>
        [JsonPropertyName("jobTitle")]
        public string? JobTitle { get; set; }

        /// <summary>Email principal (<c>emailaddress1</c>).</summary>
        [JsonPropertyName("emailAddress1")]
        public string? EmailAddress1 { get; set; }

        /// <summary>Celular (<c>mobilephone</c>).</summary>
        [JsonPropertyName("mobilePhone")]
        public string? MobilePhone { get; set; }

        /// <summary>Telefono fijo (<c>telephone1</c>).</summary>
        [JsonPropertyName("telephone1")]
        public string? Telephone1 { get; set; }

        /// <summary>Descripcion libre (<c>description</c>): la consulta original, el chat, etc.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Origen del cliente potencial (<c>leadsourcecode</c>), como valor del optionset.
        /// Va sin traducir a proposito: los valores son propios del org y mapearlos aca
        /// obligaria a redeployar la app cada vez que se agrega una opcion.
        /// </summary>
        [JsonPropertyName("leadSourceCode")]
        public int? LeadSourceCode { get; set; }

        /// <summary>Domicilio. Opcional: si no viene, no se toca ningun campo de direccion.</summary>
        [JsonPropertyName("address")]
        public LeadAddress? Address { get; set; }
    }

    /// <summary>
    /// Domicilio del lead. Mapea contra los campos nativos <c>address1_*</c> de la tabla
    /// <c>lead</c> — no contra una tabla relacionada. Por eso viaja adentro del mismo
    /// mensaje y se escribe en el mismo Create: no hay un segundo registro que pueda
    /// quedar huerfano si la segunda escritura falla.
    /// </summary>
    public sealed class LeadAddress
    {
        /// <summary>Nombre de la direccion (<c>address1_name</c>). Ej: "Casa central".</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Calle (<c>address1_line1</c>).</summary>
        [JsonPropertyName("line1")]
        public string? Line1 { get; set; }

        /// <summary>Numero, piso, depto (<c>address1_line2</c>).</summary>
        [JsonPropertyName("line2")]
        public string? Line2 { get; set; }

        /// <summary>Referencia adicional (<c>address1_line3</c>).</summary>
        [JsonPropertyName("line3")]
        public string? Line3 { get; set; }

        /// <summary>Ciudad (<c>address1_city</c>).</summary>
        [JsonPropertyName("city")]
        public string? City { get; set; }

        /// <summary>Departamento o provincia (<c>address1_stateorprovince</c>).</summary>
        [JsonPropertyName("stateOrProvince")]
        public string? StateOrProvince { get; set; }

        /// <summary>Codigo postal (<c>address1_postalcode</c>).</summary>
        [JsonPropertyName("postalCode")]
        public string? PostalCode { get; set; }

        /// <summary>Pais (<c>address1_country</c>).</summary>
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        /// <summary>Telefono del domicilio (<c>address1_telephone1</c>).</summary>
        [JsonPropertyName("telephone")]
        public string? Telephone { get; set; }
    }
}
