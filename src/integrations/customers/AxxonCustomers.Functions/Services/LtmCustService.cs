using Axxon.Eip.Core.FinOps;
using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Services
{
    public interface ILtmCustService
    {
        /// <summary>Inserta la fila de <c>LTMCustTable</c> del cliente.</summary>
        Task CreateAsync(LtmCustPayload payload, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Escribe la contraparte de localizacion PY del cliente en F&amp;O, via el cliente OData
    /// generico de la EiP (retry de throttling y clasificacion de errores incluidos).
    ///
    /// <b>Esta primera version solo hace el POST.</b> No consulta si la fila ya existe ni
    /// actualiza: se manda el JSON armado y, si F&amp;O lo rechaza, el mensaje va al DLQ como
    /// <c>BusinessRuleFailed</c> sin reintentar (decision #4) y no se procesa. La
    /// modificacion queda fuera de alcance a proposito.
    ///
    /// La consecuencia a tener presente: si la localizacion crea la fila junto con el
    /// CustTable, o si el mismo cliente se encola dos veces, el POST devuelve 400 y el
    /// mensaje termina en el DLQ. Es esperable, no un sintoma de que algo se rompio.
    /// </summary>
    public class LtmCustService : ILtmCustService
    {
        private readonly IFoODataClient _client;
        private readonly ILogger<LtmCustService> _logger;

        public LtmCustService(IFoODataClient client, ILogger<LtmCustService> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task CreateAsync(
            LtmCustPayload payload,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[LtmCustService] Insertando en {EntitySet}. AccountNum={AccountNum} | " +
                "DataAreaId={DataAreaId} | Campos={FieldCount}",
                LtmCustMapping.EntitySet, payload.AccountNum, payload.DataAreaId,
                payload.Fields.Count);

            await _client.CreateAsync<IDictionary<string, object?>, LtmCustTableRecord>(
                LtmCustMapping.EntitySet, payload.Fields, cancellationToken);

            _logger.LogInformation(
                "[LtmCustService] Fila creada en F&O. AccountNum={AccountNum} | DataAreaId={DataAreaId}",
                payload.AccountNum, payload.DataAreaId);
        }
    }
}
