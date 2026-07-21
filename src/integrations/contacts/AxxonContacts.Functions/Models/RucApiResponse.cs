namespace AxxonContacts.Functions.Models
{
    /// <summary>
    /// Respuesta de la API https://turuc.com.py/api/contribuyente/{id}
    /// </summary>
    public class RucApiResponse
    {
        public RucData? Data    { get; set; }
        public string?  Message { get; set; }
    }

    public class RucData
    {
        public long    Doc              { get; set; }
        public string? RazonSocial      { get; set; }
        public int     Dv               { get; set; }
        public string? Ruc              { get; set; }
        public string? Estado           { get; set; }
        public bool    EsPersonaJuridica { get; set; }
        public bool    EsEntidadPublica  { get; set; }
    }
}
