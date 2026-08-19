using System.ServiceModel;
using AxxonCustomers.Functions.Configuration;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Escribe <c>msdyn_sellable</c> en el contact recien calificado, con el valor que
    /// trae el App Setting <c>QualifyLeadSellableValue</c>.
    ///
    /// Por que existe: la guarda <c>syncWhen</c> del overlay del contact exige
    /// <c>msdyn_sellable = true</c> (para F&amp;O un party que no es sellable es un
    /// PROSPECTO, y mandarlo igual lo crea como prospect y rompe el alta posterior del
    /// customer con un 400). Nada en la EiP ponia ese true: dependia de que lo dejara la
    /// UI o una customizacion del environment, y si no, el contact se salteaba en
    /// silencio. Este servicio cierra ese hueco del lado del flujo de calificacion.
    ///
    /// Alcance: solo QualifyLead. fo-sync no sella nada; sigue leyendo lo que haya.
    ///
    /// Dos efectos que hay que tener presentes:
    ///   - El Update entra en los Filtering Attributes de ContactEventPublisherPlugin
    ///     (msdyn_sellable esta en la lista), asi que dispara un evento de contact hacia
    ///     la cola de master-matching. Es el mismo camino que ya se dispara cuando alguien
    ///     toca el campo a mano.
    ///   - Sobre un contact master, MasterFoIsolationPlugin lo pisa de vuelta a false: el
    ///     master no puede llegar al ERP. Es la conducta deseada, no un choque a resolver.
    /// </summary>
    public class SellableStamper : ISellableStamper
    {
        public const string SellableAttribute = "msdyn_sellable";

        private const string ContactEntity      = "contact";
        private const int    ObjectDoesNotExist = unchecked((int)0x80040217);

        private readonly IOrganizationService _orgService;
        private readonly AppSettings _settings;
        private readonly ILogger<SellableStamper> _logger;

        public SellableStamper(
            IOrganizationService orgService,
            AppSettings settings,
            ILogger<SellableStamper> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _settings   = settings   ?? throw new ArgumentNullException(nameof(settings));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool Stamp(Guid contactId)
        {
            var value = _settings.QualifyLeadSellableValue;

            if (value is null)
            {
                _logger.LogInformation(
                    "[SellableStamper] 'QualifyLeadSellableValue' sin configurar: no se toca " +
                    "{Attribute} del contact {ContactId}. Sincroniza solo si ya venia sellable.",
                    SellableAttribute, contactId);
                return false;
            }

            var update = new Entity(ContactEntity, contactId)
            {
                [SellableAttribute] = value.Value
            };

            try
            {
                _orgService.Update(update);
            }
            catch (FaultException<OrganizationServiceFault> ex)
                when (ex.Detail.ErrorCode == ObjectDoesNotExist)
            {
                // Mismo criterio que CustomerSyncService al recuperar el registro: un
                // contact que no existe no se arregla reintentando.
                throw new NonRetryableSyncException(
                    $"El contact {contactId} no existe en Dataverse: no se pudo escribir " +
                    $"{SellableAttribute}.");
            }

            _logger.LogInformation(
                "[SellableStamper] contact {ContactId} sellado con {Attribute}={Value}.",
                contactId, SellableAttribute, value.Value);

            return true;
        }
    }
}
