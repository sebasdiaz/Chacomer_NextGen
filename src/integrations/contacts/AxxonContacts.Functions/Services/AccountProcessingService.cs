using AxxonContacts.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Orquesta el flujo completo de procesamiento de un Account:
    ///   1. AccountMasterMatchingService — crea (o localiza) el master y linkea los raws.
    ///   2. SetRucValidationService      — valida el RUC contra la API SET y actualiza el master.
    /// </summary>
    public class AccountProcessingService
    {
        private readonly AccountMasterMatchingService _matchingService;
        private readonly SetRucValidationService      _setRucValidationService;
        private readonly ILogger                      _logger;

        public AccountProcessingService(
            AccountMasterMatchingService matchingService,
            SetRucValidationService      setRucValidationService,
            ILogger                      logger)
        {
            _matchingService         = matchingService         ?? throw new ArgumentNullException(nameof(matchingService));
            _setRucValidationService = setRucValidationService ?? throw new ArgumentNullException(nameof(setRucValidationService));
            _logger                  = logger                  ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessAsync(AccountEventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            _logger.LogInformation(
                "[AccountProcessingService] Inicio. Account={AccountId} | Identification={Identification} | Trigger={Trigger}",
                message.AccountId, message.MsdynIdentificationNumber, message.TriggerMessage);

            // Paso 1 — Crear o localizar el master y linkear raws
            EntityReference? masterRef = await _matchingService.ProcessAsync(message);

            // Paso 2 — Validar RUC contra SET y actualizar master
            if (masterRef != null)
                await _setRucValidationService.ValidateAndUpdateAsync(
                    "account",
                    masterRef.Id,
                    message.MsdynIdentificationNumber!);

            _logger.LogInformation(
                "[AccountProcessingService] Fin. Account={AccountId} | Master={MasterId}",
                message.AccountId, masterRef?.Id.ToString() ?? "N/A");
        }
    }
}
