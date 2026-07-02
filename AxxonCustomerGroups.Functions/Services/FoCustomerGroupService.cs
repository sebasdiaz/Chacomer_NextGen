using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AxxonCustomerGroups.Functions.Configuration;
using AxxonCustomerGroups.Functions.Models;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace AxxonCustomerGroups.Functions.Services
{
    /// <summary>
    /// Servicio que consume la OData API de Finance &amp; Operations para leer CustomerGroups.
    /// Autentica con Managed Identity (o Client Secret en DESA) via OAuth2 contra el tenant de F&O.
    /// Usa cross-company=true para obtener registros de todas las companias del ambiente.
    /// Soporta paginacion con @odata.nextLink.
    /// </summary>
    public class FoCustomerGroupService : IFoCustomerGroupService
    {
        private const string EntitySet = "CustomerGroups";
        private const int PageSize = 1000;

        private readonly HttpClient _httpClient;
        private readonly AppSettings _settings;
        private readonly ILogger<FoCustomerGroupService> _logger;
        private readonly TokenCredential _credential;

        public FoCustomerGroupService(HttpClient httpClient, AppSettings settings, ILogger<FoCustomerGroupService> logger)
        {
            _httpClient = httpClient;
            _settings   = settings;
            _logger     = logger;
            _credential = settings.UseClientSecretAuth
                ? new ClientSecretCredential(
                    settings.FoTenantId,
                    settings.FoClientId,
                    settings.FoClientSecret)
                : new DefaultAzureCredential();
        }

        public async IAsyncEnumerable<FoCustomerGroup> GetCustomerGroupsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var initialUrl = BuildInitialUrl();
            string? nextLink = initialUrl;
            var pageNumber  = 0;

            while (nextLink is not null && !cancellationToken.IsCancellationRequested)
            {
                pageNumber++;
                _logger.LogInformation(
                    "[FoCustomerGroupService] Leyendo pagina {Page} desde F&O. URL={Url}",
                    pageNumber, nextLink);

                var response = await FetchPageAsync(nextLink, cancellationToken);

                if (response?.Value is null || response.Value.Count == 0)
                    yield break;

                _logger.LogInformation(
                    "[FoCustomerGroupService] Pagina {Page}: {Count} registros recibidos.",
                    pageNumber, response.Value.Count);

                foreach (var group in response.Value)
                    yield return group;

                nextLink = response.NextLink;
            }
        }

        private async Task<FoODataResponse<FoCustomerGroup>?> FetchPageAsync(
            string url,
            CancellationToken cancellationToken)
        {
            var token = await GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "[FoCustomerGroupService] Error HTTP {Status} al consultar F&O. Body={Body}",
                    httpResponse.StatusCode, body);
                throw new HttpRequestException(
                    $"F&O OData respondio con HTTP {(int)httpResponse.StatusCode}: {body}");
            }

            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FoODataResponse<FoCustomerGroup>>(content);
        }

        private string BuildInitialUrl()
        {
            var baseUrl = _settings.FoBaseUrl.TrimEnd('/');
            var url     = $"{baseUrl}/data/{EntitySet}?cross-company=true&$top={PageSize}";

            // Excluye las legal entities que ya sincroniza Dual Write: el filtro
            // se aplica server-side para no leer esos registros de F&O.
            if (_settings.DualWriteLegalEntities.Count > 0)
            {
                var conditions = _settings.DualWriteLegalEntities
                    .Select(le => $"dataAreaId ne '{EscapeODataLiteral(le)}'");
                var filter = string.Join(" and ", conditions);

                url += $"&$filter={Uri.EscapeDataString(filter)}";

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

            return url;
        }

        // OData escapa la comilla simple duplicandola dentro del literal.
        private static string EscapeODataLiteral(string value) => value.Replace("'", "''");

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            var resource = _settings.FoBaseUrl.TrimEnd('/');
            var context  = new TokenRequestContext(new[] { $"{resource}/.default" });
            var token    = await _credential.GetTokenAsync(context, cancellationToken);
            return token.Token;
        }
    }
}
