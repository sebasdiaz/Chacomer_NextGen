namespace AxxonLeads.Functions.Models
{
    /// <summary>
    /// Indica un error de datos que no se resuelve reintentando (ej. una columna que no
    /// existe en el org, un optionset con un valor invalido).
    /// La Function envia el mensaje directo al DLQ en lugar de abandonarlo.
    /// </summary>
    public class NonRetryableLeadException : Exception
    {
        public NonRetryableLeadException(string message) : base(message)
        {
        }
    }
}
