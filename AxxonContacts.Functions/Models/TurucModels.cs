using System.Text.Json.Serialization;

namespace AxxonContacts.Functions.Models
{
    // ── DTO compartido ────────────────────────────────────────────────

    public class ContribuyenteDto
    {
        [JsonPropertyName("doc")]
        public long Doc { get; set; }

        [JsonPropertyName("razonSocial")]
        public string? RazonSocial { get; set; }

        [JsonPropertyName("dv")]
        public int Dv { get; set; }

        [JsonPropertyName("ruc")]
        public string? Ruc { get; set; }

        [JsonPropertyName("estado")]
        public string? Estado { get; set; }

        [JsonPropertyName("esPersonaJuridica")]
        public bool EsPersonaJuridica { get; set; }

        [JsonPropertyName("esEntidadPublica")]
        public bool EsEntidadPublica { get; set; }
    }

    // ── GET /api/contribuyente  y  /api/contribuyente/{ruc} ──────────

    public class ContribuyenteResponse
    {
        [JsonPropertyName("data")]
        public ContribuyenteDto? Data { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    // ── GET /api/contribuyente/search ────────────────────────────────

    public class SearchData
    {
        [JsonPropertyName("contribuyentes")]
        public List<ContribuyenteDto>? Contribuyentes { get; set; }

        [JsonPropertyName("paginas")]
        public int Paginas { get; set; }
    }

    public class SearchContribuyentesResponse
    {
        [JsonPropertyName("data")]
        public SearchData? Data { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    // ── GET /api/contribuyente/table ─────────────────────────────────

    public class DataTableResponse
    {
        [JsonPropertyName("draw")]
        public int Draw { get; set; }

        [JsonPropertyName("recordsTotal")]
        public long RecordsTotal { get; set; }

        [JsonPropertyName("recordsFiltered")]
        public long RecordsFiltered { get; set; }

        [JsonPropertyName("data")]
        public List<ContribuyenteDto>? Data { get; set; }
    }

    // ── GET /api/contribuyente/persona-juridica ──────────────────────

    public class PersonaJuridicaDto
    {
        [JsonPropertyName("razonSocial")]
        public string? RazonSocial { get; set; }

        [JsonPropertyName("ruc")]
        public long Ruc { get; set; }

        [JsonPropertyName("dv")]
        public int Dv { get; set; }

        [JsonPropertyName("estado")]
        public string? Estado { get; set; }

        [JsonPropertyName("categoria")]
        public string? Categoria { get; set; }
    }

    public class PersonaJuridicaResponse
    {
        [JsonPropertyName("data")]
        public PersonaJuridicaDto? Data { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    // ── GET /api/contribuyente/entidad-publica ───────────────────────

    public class EntidadPublicaDto
    {
        [JsonPropertyName("razonSocial")]
        public string? RazonSocial { get; set; }

        [JsonPropertyName("ruc")]
        public long Ruc { get; set; }

        [JsonPropertyName("dv")]
        public int Dv { get; set; }

        [JsonPropertyName("nivel")]
        public int Nivel { get; set; }

        [JsonPropertyName("estado")]
        public string? Estado { get; set; }

        [JsonPropertyName("descripcionNivel")]
        public string? DescripcionNivel { get; set; }

        [JsonPropertyName("codigoEntidad")]
        public string? CodigoEntidad { get; set; }

        [JsonPropertyName("paginaWeb")]
        public string? PaginaWeb { get; set; }

        [JsonPropertyName("correo")]
        public string? Correo { get; set; }

        [JsonPropertyName("direccion")]
        public string? Direccion { get; set; }

        [JsonPropertyName("telefono")]
        public string? Telefono { get; set; }

        [JsonPropertyName("sigla")]
        public string? Sigla { get; set; }

        [JsonPropertyName("anho")]
        public int Anho { get; set; }
    }

    public class EntidadPublicaResponse
    {
        [JsonPropertyName("data")]
        public EntidadPublicaDto? Data { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
