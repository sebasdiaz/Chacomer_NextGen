namespace AxxonCustomers.Functions.Services
{
    public interface IContactCustomerSyncService
    {
        /// <summary>
        /// Lee el contact desde Dataverse, lo mapea segun CustomersV3_Contact.json
        /// y lo inserta en la entidad CustomersV3 de F&O. Luego escribe el
        /// CustomerAccount generado en msdyn_contactpersonid del contact.
        /// </summary>
        Task ProcessAsync(Guid contactId, CancellationToken cancellationToken = default);
    }
}
