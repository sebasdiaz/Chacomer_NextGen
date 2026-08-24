using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axxon.Eip.Core.Configuration;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.Dataverse
{
    /// <inheritdoc cref="IDataverseWebApiClient"/>
    public sealed class DataverseWebApiClient : IDataverseWebApiClient
    {
        /// <summary>Nombre del HttpClient en IHttpClientFactory.</summary>
        public const string HttpClientName = "DataverseWebApi";

        /// <summary>Version de la Web API contra la que se arman las rutas relativas.</summary>
        public const string ApiPath = "/api/data/v9.2/";

        // IHttpClientFactory y no un HttpClient capturado: el servicio es singleton, y
        // aferrarse a una instancia por toda la vida del proceso saltea la rotacion de
        // handlers de la factory (DNS que cambia, sockets que envejecen).
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TokenCredential _credential;
        private readonly DataverseOptions _options;
        private readonly ILogger<DataverseWebApiClient> _logger;
        private readonly string[] _scopes;

        public DataverseWebApiClient(
            IHttpClientFactory httpClientFactory,
            TokenCredential credential,
            DataverseOptions options,
            ILogger<DataverseWebApiClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _credential        = credential;
            _options           = options;
            _logger            = logger;
            _scopes            = new[] { $"{options.Url.TrimEnd('/')}/.default" };
        }

        public async Task<JsonElement?> GetRecordAsync(
            string relativeUrl, string label, CancellationToken cancellationToken = default)
        {
            using var response = await SendAsync(HttpMethod.Get, relativeUrl, content: null, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[{Label}] Dataverse devolvio 404 para {Url}.", label, relativeUrl);
                return null;
            }

            var json = await EnsureSuccessAsync(response, label, relativeUrl, cancellationToken);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        public async Task<IReadOnlyList<JsonElement>> GetArrayAsync(
            string relativeUrl, string label, CancellationToken cancellationToken = default)
        {
            using var response = await SendAsync(HttpMethod.Get, relativeUrl, content: null, cancellationToken);
            var json = await EnsureSuccessAsync(response, label, relativeUrl, cancellationToken);

            var root = JsonSerializer.Deserialize<JsonElement>(json);

            if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                throw new DataverseWebApiException(label, response.StatusCode, relativeUrl,
                    "La respuesta no trae la propiedad 'value' esperada de una coleccion OData.");

            var items = value.EnumerateArray().ToList();
            _logger.LogInformation("[{Label}] {Count} registros.", label, items.Count);
            return items;
        }

        public async Task<Guid> CreateAsync(
            string entitySet, object payload, string label, CancellationToken cancellationToken = default)
        {
            using var content  = JsonContent.Create(payload);
            using var response = await SendAsync(HttpMethod.Post, entitySet, content, cancellationToken);
            await EnsureSuccessAsync(response, label, entitySet, cancellationToken);

            // Dataverse devuelve el id creado en el header OData-EntityId:
            //   https://org.crm.dynamics.com/api/data/v9.2/entityset(00000000-0000-0000-0000-000000000000)
            if (!response.Headers.TryGetValues("OData-EntityId", out var values))
                throw new DataverseWebApiException(label, response.StatusCode, entitySet,
                    "El POST no devolvio el header 'OData-EntityId' con el id del registro creado.");

            var entityId = values.First();
            var open     = entityId.LastIndexOf('(');
            var close    = entityId.LastIndexOf(')');

            if (open < 0 || close <= open || !Guid.TryParse(entityId[(open + 1)..close], out var id))
                throw new DataverseWebApiException(label, response.StatusCode, entitySet,
                    $"No se pudo extraer el GUID de 'OData-EntityId': {entityId}");

            _logger.LogInformation("[{Label}] Creado {EntitySet} {Id}.", label, entitySet, id);
            return id;
        }

        // El token va en el HttpRequestMessage, NUNCA en DefaultRequestHeaders: el HttpClient
        // lo comparten todas las invocaciones de la instancia, asi que mutarle los headers
        // hace que dos requests concurrentes se pisen el Authorization.
        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string relativeUrl, HttpContent? content, CancellationToken cancellationToken)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(_scopes), cancellationToken);

            using var request = new HttpRequestMessage(method, relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("Prefer", "odata.include-annotations=*");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (content is not null)
                request.Content = content;

            var http = _httpClientFactory.CreateClient(HttpClientName);
            return await http.SendAsync(request, cancellationToken);
        }

        private static async Task<string> EnsureSuccessAsync(
            HttpResponseMessage response, string label, string relativeUrl, CancellationToken cancellationToken)
        {
            var body = response.Content.Headers.ContentLength == 0
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new DataverseWebApiException(label, response.StatusCode, relativeUrl, body);

            return body;
        }
    }
}
