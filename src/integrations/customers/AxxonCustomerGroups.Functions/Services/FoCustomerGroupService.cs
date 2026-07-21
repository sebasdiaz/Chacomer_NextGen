using Axxon.Eip.Core.FinOps;
using AxxonCustomerGroups.Functions.Configuration;
using AxxonCustomerGroups.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonCustomerGroups.Functions.Services
{
    /// <summary>
    /// Lee CustomerGroups de F&amp;O via el cliente OData generico de la EiP
    /// (cross-company, paginacion con @odata.nextLink y retry de throttling),
    /// excluyendo las legal entities que ya sincroniza Dual Write.
    /// </summary>
    public class FoCustomerGroupService : IFoCustomerGroupService
    {
        private const string EntitySet = "CustomerGroups";

        private readonly IFoODataClient _client;
        private readonly AppSettings _settings;
        private readonly ILogger<FoCustomerGroupService> _logger;

        public FoCustomerGroupService(
            IFoODataClient client,
            AppSettings settings,
            ILogger<FoCustomerGroupService> logger)
        {
            _client   = client;
            _settings = settings;
            _logger   = logger;
        }

        public IAsyncEnumerable<FoCustomerGroup> GetCustomerGroupsAsync(
            CancellationToken cancellationToken = default)
        {
            string? filter = null;

            // Excluye las legal entities que ya sincroniza Dual Write: el filtro
            // se aplica server-side para no leer esos registros de F&O.
            if (_settings.DualWriteLegalEntities.Count > 0)
            {
                filter = string.Join(" and ", _settings.DualWriteLegalEntities
                    .Select(le => $"dataAreaId ne '{FoOData.EscapeLiteral(le)}'"));

                _logger.LogInformation(
                    "[FoCustomerGroupService] Excluyendo legal entities sincronizadas por " +
                    "Dual Write: [{LegalEntities}]",
                    string.Join(", ", _settings.DualWriteLegalEntities));
            }
            else
            {
                _logger.LogInformation(
                    "[FoCustomerGroupService] 'DualWriteLegalEntities' vacio: se sincronizan " +
                    "los customer groups de TODAS las companias.");
            }

            return _client.QueryAsync<FoCustomerGroup>(
                new FoODataQuery(EntitySet) { Filter = filter },
                cancellationToken);
        }
    }
}
