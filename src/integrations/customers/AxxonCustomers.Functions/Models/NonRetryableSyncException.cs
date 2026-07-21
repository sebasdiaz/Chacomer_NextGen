namespace AxxonCustomers.Functions.Models
{
    /// <summary>
    /// Indica un error de datos que no se resuelve reintentando
    /// (ej. contact inexistente, sin compania asignada).
    /// La Function envia el mensaje directo al DLQ en lugar de abandonarlo.
    /// </summary>
    public class NonRetryableSyncException : Exception
    {
        public NonRetryableSyncException(string message) : base(message)
        {
        }
    }
}
