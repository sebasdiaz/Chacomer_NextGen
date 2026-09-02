using System.Text.Json.Serialization;

namespace AxxonCustomerCredit.Functions.Models
{
    /// <summary>
    /// Un registro de la entidad <c>DevAxCustCreditCustomers</c> de F&amp;O: la ficha
    /// crediticia del cliente (datos personales, laborales y de ingresos con los que el
    /// analista evalua una solicitud).
    ///
    /// Los nombres viajan tal cual los expone F&amp;O, a proposito: este endpoint no
    /// traduce el modelo del ERP, lo publica. Ver
    /// docs/wiki/integraciones/customercredit.md.
    /// </summary>
    public sealed class FoCreditoCliente
    {
        // --- Clave de la entidad: (dataAreaId, CustomerAccount) ---

        [JsonPropertyName("dataAreaId")]
        public string DataAreaId { get; set; } = string.Empty;

        [JsonPropertyName("CustomerAccount")]
        public string CustomerAccount { get; set; } = string.Empty;

        [JsonPropertyName("PartyNumber")]
        public string? PartyNumber { get; set; }

        [JsonPropertyName("FullName")]
        public string? FullName { get; set; }

        // --- Identificacion ---

        [JsonPropertyName("IdentityDocumentType")]
        public string? IdentityDocumentType { get; set; }

        [JsonPropertyName("IdentityDocumentNumber")]
        public string? IdentityDocumentNumber { get; set; }

        [JsonPropertyName("Nationality")]
        public string? Nationality { get; set; }

        /// <summary>Enum <c>Gender</c> de F&amp;O: "Unknown" / "Male" / "Female" / "NonSpecific".</summary>
        [JsonPropertyName("Gender")]
        public string? Gender { get; set; }

        /// <summary>Enum <c>DirPersonMaritalStatus</c> de F&amp;O ("None", "Married", ...).</summary>
        [JsonPropertyName("MaritalStatus")]
        public string? MaritalStatus { get; set; }

        // La fecha de nacimiento viene desarmada en tres campos, no como fecha: el mes es
        // el enum MonthsOfYear ("None" cuando no esta cargado) y el dia y el ano son
        // enteros que valen 0 cuando faltan. Se publican tal cual: armar una fecha con
        // partes incompletas seria inventar un dato que F&O no tiene.

        [JsonPropertyName("BirthDay")]
        public int BirthDay { get; set; }

        [JsonPropertyName("BirthMonth")]
        public string? BirthMonth { get; set; }

        [JsonPropertyName("BirthYear")]
        public int BirthYear { get; set; }

        // --- Contacto y domicilio ---

        [JsonPropertyName("Email")]
        public string? Email { get; set; }

        [JsonPropertyName("Phone")]
        public string? Phone { get; set; }

        [JsonPropertyName("WorkPhone")]
        public string? WorkPhone { get; set; }

        /// <summary>Direccion primaria ya formateada por F&amp;O: viene multilinea.</summary>
        [JsonPropertyName("PrimaryAddress")]
        public string? PrimaryAddress { get; set; }

        [JsonPropertyName("WorkAddress")]
        public string? WorkAddress { get; set; }

        [JsonPropertyName("Latitude")]
        public decimal Latitude { get; set; }

        [JsonPropertyName("Longitude")]
        public decimal Longitude { get; set; }

        /// <summary>
        /// Vigencia del domicilio. F&amp;O usa fechas centinela en vez de null:
        /// <c>1900-01-01</c> para "sin definir" y <c>2154-12-31T23:59:59Z</c> para "sin
        /// vencimiento". Se publican como vienen.
        /// </summary>
        [JsonPropertyName("AddressValidFrom")]
        public DateTimeOffset? AddressValidFrom { get; set; }

        [JsonPropertyName("AddressValidTo")]
        public DateTimeOffset? AddressValidTo { get; set; }

        // --- Situacion laboral e ingresos ---

        [JsonPropertyName("EmployerName")]
        public string? EmployerName { get; set; }

        [JsonPropertyName("JobPosition")]
        public string? JobPosition { get; set; }

        [JsonPropertyName("BranchOfActivity")]
        public string? BranchOfActivity { get; set; }

        /// <summary>Antiguedad laboral, en la unidad con la que la carga F&amp;O.</summary>
        [JsonPropertyName("Seniority")]
        public int Seniority { get; set; }

        [JsonPropertyName("IncomeAmount")]
        public decimal IncomeAmount { get; set; }

        /// <summary>Moneda de <see cref="IncomeAmount"/>. Vacia si el ingreso no fue cargado.</summary>
        [JsonPropertyName("IncomeCurrencyCode")]
        public string? IncomeCurrencyCode { get; set; }

        [JsonPropertyName("Qualification")]
        public string? Qualification { get; set; }

        // --- Marcas de riesgo ---

        /// <summary>Enum <c>NoYes</c> de F&amp;O: "Yes" / "No".</summary>
        [JsonPropertyName("Homeowner")]
        public string? Homeowner { get; set; }

        /// <summary>Enum <c>NoYes</c>. Persona expuesta politicamente.</summary>
        [JsonPropertyName("PoliticallyExposed")]
        public string? PoliticallyExposed { get; set; }

        /// <summary>Enum <c>NoYes</c>. Opera dentro de un grupo economico.</summary>
        [JsonPropertyName("OperatesInGroup")]
        public string? OperatesInGroup { get; set; }

        [JsonPropertyName("ClassificationGroupId")]
        public string? ClassificationGroupId { get; set; }
    }
}
