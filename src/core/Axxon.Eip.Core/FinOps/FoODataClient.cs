using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Axxon.Eip.Core.Configuration;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.FinOps
{
    /// <summary>
    /// Implementacion de IFoODataClient sobre HttpClient.
    /// Usar el HttpClient nombrado <see cref="HttpClientName"/>, que trae los
    /// headers OData y el retry de throttling (429/Retry-After) configurados
    /// por AddEipFoOData.
    /// </summary>
    public class FoODataClient : IFoODataClient
    {
        public const string HttpClientName = "FoOData";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false
        };

        private readonly HttpClient _httpClient;
        private readonly FoODataOptions _options;
        private readonly ILogger<FoODataClient> _logger;
        private readonly TokenCredential _credential;

        public FoODataClient(HttpClient httpClient, FoODataOptions options, ILogger<FoODataClient> logger)
        {
            _httpClient = httpClient;
            _options    = options;
            _logger     = logger;
            _credential = options.UseClientSecretAuth
                ? new ClientSecretCredential(
                    options.TenantId,
                    options.ClientId,
                    options.ClientSecret)
                : new DefaultAzureCredential();
        }

        public async IAsyncEnumerable<T> QueryAsync<T>(
            FoODataQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string? nextLink = BuildUrl(query, top: query.PageSize);
            var pageNumber = 0;

            while (nextLink is not null && !cancellationToken.IsCancellationRequested)
            {
                pageNumber++;
                _logger.LogInformation(
                    "[FoODataClient] Leyendo pagina {Page} de {EntitySet} desde F&O. URL={Url}",
                    pageNumber, query.EntitySet, nextLink);

                var response = await FetchPageAsync<T>(nextLink, cancellationToken);

                if (response?.Value is null || response.Value.Count == 0)
                    yield break;

                _logger.LogInformation(
                    "[FoODataClient] Pagina {Page} de {EntitySet}: {Count} registros recibidos.",
                    pageNumber, query.EntitySet, response.Value.Count);

                foreach (var item in response.Value)
                    yield return item;

                nextLink = response.NextLink;
            }
        }

        public async Task<T?> FindFirstAsync<T>(
            FoODataQuery query,
            CancellationToken cancellationToken = default) where T : class
        {
            var url = BuildUrl(query, top: 1);

            _logger.LogInformation(
                "[FoODataClient] GET FindFirst en {EntitySet}. Filter: {Filter}",
                query.EntitySet, query.Filter ?? "(sin filtro)");

            var response = await FetchPageAsync<T>(url, cancellationToken);

            return response?.Value is { Count: > 0 } ? response.Value[0] : null;
        }

        public async Task<TResponse> CreateAsync<TEntity, TResponse>(
            string entitySet,
            TEntity entity,
            CancellationToken cancellationToken = default)
        {
            var url   = $"{BaseUrl()}/data/{entitySet}";
            var token = await GetAccessTokenAsync(cancellationToken);
            var body  = JsonSerializer.Serialize(entity, SerializerOptions);

            _logger.LogInformation("[FoODataClient] POST {Url}.", url);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var failure = FoODataException.FromResponse(httpResponse.StatusCode, entitySet, content);

                _logger.LogError(
                    "[FoODataClient] Error HTTP {Status} al insertar en {EntitySet} " +
                    "(permanente={IsPermanent}). F&O dijo: {FoMessage} | Body={Body}",
                    httpResponse.StatusCode, entitySet, failure.IsPermanent,
                    failure.FoMessage ?? "(sin mensaje de negocio)", content);

                throw failure;
            }

            return JsonSerializer.Deserialize<TResponse>(content)
                ?? throw new InvalidOperationException(
                    $"F&O devolvio una respuesta vacia al crear el registro en {entitySet}.");
        }

        private async Task<FoODataResponse<T>?> FetchPageAsync<T>(
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
                var body    = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                var failure = FoODataException.FromResponse(httpResponse.StatusCode, url, body);

                _logger.LogError(
                    "[FoODataClient] Error HTTP {Status} al consultar F&O " +
                    "(permanente={IsPermanent}). Body={Body}",
                    httpResponse.StatusCode, failure.IsPermanent, body);

                throw failure;
            }

            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<FoODataResponse<T>>(content);
        }

        private string BuildUrl(FoODataQuery query, int top)
        {
            var url = $"{BaseUrl()}/data/{query.EntitySet}?$top={top}";

            if (query.CrossCompany)
                url += "&cross-company=true";

            if (!string.IsNullOrWhiteSpace(query.Filter))
                url += $"&$filter={Uri.EscapeDataString(query.Filter)}";

            if (!string.IsNullOrWhiteSpace(query.Select))
                url += $"&$select={query.Select}";

            return url;
        }

        private string BaseUrl() => _options.BaseUrl.TrimEnd('/');

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            var context = new TokenRequestContext(new[] { $"{BaseUrl()}/.default" });
            var token   = await _credential.GetTokenAsync(context, cancellationToken);
            return token.Token;
        }
    }
}
