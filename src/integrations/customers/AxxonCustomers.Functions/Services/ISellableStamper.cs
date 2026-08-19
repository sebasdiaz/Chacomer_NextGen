namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Escribe <c>msdyn_sellable</c> en el contact que se acaba de calificar.
    /// </summary>
    public interface ISellableStamper
    {
        /// <summary>
        /// Sella el contact con el valor configurado. No hace nada si el App Setting
        /// <c>QualifyLeadSellableValue</c> no esta seteado.
        /// </summary>
        /// <returns>true si escribio en Dataverse.</returns>
        bool Stamp(Guid contactId);
    }
}
