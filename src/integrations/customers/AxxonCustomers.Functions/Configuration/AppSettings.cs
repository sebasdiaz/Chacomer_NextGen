namespace AxxonCustomers.Functions.Configuration
{
    /// <summary>
    /// Settings propios de esta integracion. La conexion a Dataverse y F&amp;O se
    /// configura via Axxon.Eip.Core (DataverseUrl, FoBaseUrl, etc.).
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Valor que se escribe en <c>msdyn_sellable</c> del contact al calificar el
        /// prospecto (QualifyLead), antes de evaluar la guarda <c>syncWhen</c> del mapeo.
        /// Se setea via App Setting <c>QualifyLeadSellableValue</c> ("true" / "false").
        ///
        /// <c>null</c> = el setting no esta o no es un booleano: no se escribe nada y el
        /// contact sincroniza solo si ya venia sellable, que es el comportamiento
        /// historico. Ese es el interruptor para apagar el sellado sin tocar codigo.
        /// </summary>
        public bool? QualifyLeadSellableValue { get; set; }

        /// <summary>
        /// Lee el App Setting del sellado. Lo que no parsea como booleano cae en null
        /// (no sellar) y NO en false: un valor mal escrito que se leyera como false
        /// sellaria todos los contacts como no sellables y cortaria la sincronizacion
        /// entera en silencio.
        /// </summary>
        public static bool? ParseSellableValue(string? raw) =>
            bool.TryParse(raw, out var value) ? value : null;
    }
}
