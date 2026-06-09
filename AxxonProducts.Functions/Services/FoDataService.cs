using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AxxonProducts.Functions.Configuration;
using AxxonProducts.Functions.Models;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace AxxonProducts.Functions.Services
{
    /// <summary>
    /// Servicio que consume la OData API de Finance & Operations para leer ReleasedProductsV2.
    /// Autentica con Managed Identity (o Client Secret en DESA) via OAuth2 contra el tenant de F&O.
    /// Usa cross-company=true para obtener registros de todas las companias del ambiente.
    /// Soporta paginacion con @odata.nextLink para datasets grandes.
    /// </summary>
    public class FoDataService : IFoDataService
    {
        private const string EntitySet = "ReleasedProductsV2";
        private const int PageSize = 1000;

        private readonly HttpClient _httpClient;
        private readonly AppSettings _settings;
        private readonly ILogger<FoDataService> _logger;
        private readonly TokenCredential _credential;

        public FoDataService(HttpClient httpClient, AppSettings settings, ILogger<FoDataService> logger)
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

        public async IAsyncEnumerable<FoReleasedProduct> GetReleasedProductsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var initialUrl = BuildInitialUrl();
            string? nextLink = initialUrl;
            var pageNumber  = 0;

            while (nextLink is not null && !cancellationToken.IsCancellationRequested)
            {
                pageNumber++;
                _logger.LogInformation(
                    "[FoDataService] Leyendo pagina {Page} desde F&O. URL={Url}",
                    pageNumber, nextLink);

                var response = await FetchPageAsync(nextLink, cancellationToken);

                if (response?.Value is null || response.Value.Count == 0)
                    yield break;

                _logger.LogInformation(
                    "[FoDataService] Pagina {Page}: {Count} registros recibidos.",
                    pageNumber, response.Value.Count);

                foreach (var product in response.Value)
                    yield return product;

                nextLink = response.NextLink;
            }
        }

        private async Task<FoODataResponse<FoReleasedProduct>?> FetchPageAsync(
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
                    "[FoDataService] Error HTTP {Status} al consultar F&O. Body={Body}",
                    httpResponse.StatusCode, body);
                throw new HttpRequestException(
                    $"F&O OData respondio con HTTP {(int)httpResponse.StatusCode}: {body}");
            }

            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FoODataResponse<FoReleasedProduct>>(content);
        }

        private string BuildInitialUrl()
        {
            var baseUrl = _settings.FoBaseUrl.TrimEnd('/');
            return $"{baseUrl}/data/{EntitySet}?cross-company=true&$top={PageSize}";
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            var resource = _settings.FoBaseUrl.TrimEnd('/');
            var context  = new TokenRequestContext(new[] { $"{resource}/.default" });
            var token    = await _credential.GetTokenAsync(context, cancellationToken);
            return token.Token;
        }
    }
}
