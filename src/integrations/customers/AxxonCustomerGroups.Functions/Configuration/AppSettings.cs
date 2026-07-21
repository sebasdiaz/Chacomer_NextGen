namespace AxxonCustomerGroups.Functions.Configuration
{
    /// <summary>
    /// Settings propios de esta integracion. La conexion a Dataverse y F&amp;O
    /// se configura via Axxon.Eip.Core (DataverseUrl, FoBaseUrl, etc.).
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// dataAreaIds (legal entities) que ya sincroniza Dual Write y este sync
        /// debe excluir. Se setea via App Setting "DualWriteLegalEntities" como
        /// lista separada por comas (ej: "cha,cne"). Vacio = se sincronizan todas.
        /// </summary>
        public IReadOnlyList<string> DualWriteLegalEntities { get; set; } = Array.Empty<string>();
    }
}
