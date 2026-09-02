using System.ServiceModel;
using AxxonLeads.Functions.Configuration;
using AxxonLeads.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonLeads.Functions.Services
{
    /// <summary>
    /// Crea el lead en Dataverse, deduplicando por el id del sistema origen cuando el org
    /// tiene donde guardarlo (<see cref="LeadIntakeOptions.ExternalIdAttribute"/>).
    ///
    /// La deduplicacion es lo que hace segura la reentrega. Service Bus es at-least-once:
    /// si el Create sale bien y despues se pierde el lock, el mismo mensaje vuelve. Sin
    /// buscar antes, cada reentrega es un lead duplicado que despues alguien tiene que
    /// limpiar a mano.
    /// </summary>
    public sealed class LeadIntakeService : ILeadIntakeService
    {
        /// <summary>Codigo de Dataverse para un atributo que no existe en la entidad.</summary>
        private const int AttributeNotFound = -2147217149;

        private readonly IOrganizationService _orgService;
        private readonly LeadEntityBuilder _builder;
        private readonly LeadIntakeOptions _options;
        private readonly ILogger<LeadIntakeService> _logger;

        public LeadIntakeService(
            IOrganizationService orgService,
            LeadEntityBuilder builder,
            LeadIntakeOptions options,
            ILogger<LeadIntakeService> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _builder    = builder    ?? throw new ArgumentNullException(nameof(builder));
            _options    = options    ?? throw new ArgumentNullException(nameof(options));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<LeadIntakeResult> ProcessAsync(
            string source,
            LeadIntakePayload payload,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(payload);

            var existing = FindByExternalId(payload.ExternalId);

            if (existing is { } existingId)
            {
                _logger.LogInformation(
                    "[LeadIntakeService] El lead de {Source} con externalId '{ExternalId}' ya existe " +
                    "({LeadId}). No se crea de nuevo.",
                    source, payload.ExternalId, existingId);

                return Task.FromResult(new LeadIntakeResult(existingId, AlreadyExisted: true));
            }

            var lead = _builder.Build(payload);

            Guid leadId;
            try
            {
                leadId = _orgService.Create(lead);
            }
            catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail.ErrorCode == AttributeNotFound)
            {
                // Tipicamente LeadIdentificationAttribute o LeadExternalIdAttribute apuntando
                // a una columna que no existe en este org. Reintentar no lo arregla: se
                // arregla en el Bicep. El mensaje va al DLQ diciendo exactamente eso.
                throw new NonRetryableLeadException(
                    $"Dataverse rechazo el lead: hay una columna del mapeo que no existe en la " +
                    $"entidad 'lead' de este environment. Revisar los app settings " +
                    $"'LeadIdentificationAttribute' (= '{_options.IdentificationAttribute}') y " +
                    $"'LeadExternalIdAttribute' (= '{_options.ExternalIdAttribute ?? "sin configurar"}'). " +
                    $"Dataverse dijo: {ex.Detail.Message}");
            }

            _logger.LogInformation(
                "[LeadIntakeService] Lead {LeadId} creado desde {Source}. externalId='{ExternalId}' | " +
                "identificacion='{Identification}' | domicilio={HasAddress}",
                leadId, source, payload.ExternalId ?? "(sin id de origen)",
                payload.IdentificationNumber, payload.Address is not null);

            return Task.FromResult(new LeadIntakeResult(leadId, AlreadyExisted: false));
        }

        /// <summary>
        /// Busca un lead ya creado por el id del sistema origen. Devuelve null cuando la
        /// deduplicacion esta apagada o el mensaje no trae <c>externalId</c> — en los dos
        /// casos no hay por donde buscar, y crear es lo unico que se puede hacer.
        /// </summary>
        private Guid? FindByExternalId(string? externalId)
        {
            if (!_options.DeduplicationEnabled || string.IsNullOrWhiteSpace(externalId))
                return null;

            var query = new QueryExpression(LeadEntityBuilder.LeadEntity)
            {
                // Solo interesa si existe y cual es: el id viene igual en la columna clave.
                ColumnSet = new ColumnSet(false),
                TopCount  = 1,
                Criteria  =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            _options.ExternalIdAttribute, ConditionOperator.Equal, externalId.Trim())
                    }
                }
            };

            var found = _orgService.RetrieveMultiple(query).Entities.FirstOrDefault();

            return found?.Id;
        }
    }
}
