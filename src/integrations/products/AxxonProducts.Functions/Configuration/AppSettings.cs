namespace AxxonProducts.Functions.Configuration
{
    /// <summary>
    /// Settings propios de esta integracion. La conexion a Dataverse y F&amp;O
    /// se configura via Axxon.Eip.Core (DataverseUrl, FoBaseUrl, etc.).
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Si es true, ProductGroupSyncService ejecuta AssignRequest para setear
        /// owningbusinessunit/owningteam por el team por defecto de la BU correspondiente
        /// al dataAreaId. Requiere que el default team de cada BU tenga prvRead sobre
        /// msdyn_productgroup.
        /// </summary>
        public bool AssignOwningBusinessUnit { get; set; } = false;
    }
}
