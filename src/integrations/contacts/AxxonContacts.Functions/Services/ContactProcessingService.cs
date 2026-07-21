using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Orquesta el flujo completo de procesamiento de un contacto:
    ///   1. MasterMatchingService     — crea (o localiza) el master y linkea los raws.
    ///   2. SetRucValidationService   — valida el RUC contra la API SET y actualiza el master.
    ///
    /// Al encapsular la orquestacion aqui, cualquier trigger (Service Bus, HTTP, Timer, etc.)
    /// puede reutilizar el mismo flujo inyectando unicamente este servicio.
    /// </summary>
    public class ContactProcessingService
    {
        private readonly MasterMatchingService   _masterMatchingService;
        private readonly SetRucValidationService _setRucValidationService;
        private readonly ILogger                 _logger;

        public ContactProcessingService(
            MasterMatchingService   masterMatchingService,
            SetRucValidationService setRucValidationService,
            ILogger                 logger)
        {
            _masterMatchingService   = masterMatchingService   ?? throw new ArgumentNullException(nameof(masterMatchingService));
            _setRucValidationService = setRucValidationService ?? throw new ArgumentNullException(nameof(setRucValidationService));
            _logger                  = logger                  ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Procesa el evento de contacto recibido de cualquier origen.
        /// </summary>
        public async Task ProcessAsync(ContactEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[ContactProcessingService] Inicio. Contact={ContactId} | Identification={Identification} | Trigger={Trigger}",
                message.ContactId, message.MsdynIdentificationNumber, message.TriggerMessage);

            // Paso 1 — Crear o localizar el master y linkear raws
            EntityReference? masterRef = await _masterMatchingService.ProcessAsync(message);

            // Paso 2 — Validar RUC contra SET y actualizar master
            if (masterRef != null)
                await _setRucValidationService.ValidateAndUpdateAsync(
                    "contact",
                    masterRef.Id,
                    message.MsdynIdentificationNumber!);

            _logger.LogInformation(
                "[ContactProcessingService] Fin. Contact={ContactId} | Master={MasterId}",
                message.ContactId, masterRef?.Id.ToString() ?? "N/A");
        }
    }
}
