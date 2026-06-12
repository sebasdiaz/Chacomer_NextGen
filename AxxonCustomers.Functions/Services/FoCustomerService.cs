using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AxxonCustomers.Functions.Configuration;
using AxxonCustomers.Functions.Models;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>
    /// Servicio que consume la OData API de Finance &amp; Operations para insertar
    /// registros en CustomersV3. Autentica con Managed Identity (o Client Secret
    /// en DESA) via OAuth2 contra el tenant de F&O.
    /// La compania destino se determina con dataAreaId en el body del POST.
    /// </summary>
    public class FoCustomerService : IFoCustomerService
    {
        private const string EntitySet = "CustomersV3";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false
        };

        private readonly HttpClient _httpClient;
        private readonly AppSettings _settings;
        private readonly ILogger<FoCustomerService> _logger;
        private readonly TokenCredential _credential;

        public FoCustomerService(HttpClient httpClient, AppSettings settings, ILogger<FoCustomerService> logger)
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

        public async Task<FoCustomerV3CreatedResponse> CreateCustomerAsync(
            FoCustomerV3 customer,
            CancellationToken cancellationToken = default)
        {
            var url   = $"{_settings.FoBaseUrl.TrimEnd('/')}/data/{EntitySet}";
            var token = await GetAccessTokenAsync(cancellationToken);
            var body  = JsonSerializer.Serialize(customer, SerializerOptions);

            _logger.LogInformation(
                "[FoCustomerService] POST {Url}. DataAreaId={DataAreaId} | Identification={Identification}",
                url, customer.DataAreaId, customer.IdentificationNumber);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[FoCustomerService] Error HTTP {Status} al insertar en F&O. Body={Body}",
                    httpResponse.StatusCode, content);
                throw new HttpRequestException(
                    $"F&O OData respondio con HTTP {(int)httpResponse.StatusCode}: {content}");
            }

            var created = JsonSerializer.Deserialize<FoCustomerV3CreatedResponse>(content)
                ?? throw new InvalidOperationException(
                    "F&O devolvio una respuesta vacia al crear el customer.");

            _logger.LogInformation(
                "[FoCustomerService] Customer creado en F&O. CustomerAccount={CustomerAccount} | DataAreaId={DataAreaId}",
                created.CustomerAccount, created.DataAreaId);

            return created;
        }

        public async Task<FoCustomerV3CreatedResponse?> FindCustomerAsync(
            string dataAreaId,
            string? partyNumber,
            string? customerAccount,
            CancellationToken cancellationToken = default)
        {
            // PartyNumber identifica a la persona (un party tiene a lo sumo un
            // customer por compania); CustomerAccount cubre el write-back previo.
            var criteria = new List<string>();
            if (!string.IsNullOrWhiteSpace(partyNumber))
                criteria.Add($"PartyNumber eq '{EscapeODataLiteral(partyNumber)}'");
            if (!string.IsNullOrWhiteSpace(customerAccount))
                criteria.Add($"CustomerAccount eq '{EscapeODataLiteral(customerAccount)}'");

            if (criteria.Count == 0)
                return null;

            // cross-company: sin esto F&O filtra por la compania default del caller.
            var filter = $"dataAreaId eq '{EscapeODataLiteral(dataAreaId)}' " +
                         $"and ({string.Join(" or ", criteria)})";
            var url = $"{_settings.FoBaseUrl.TrimEnd('/')}/data/{EntitySet}" +
                      $"?cross-company=true" +
                      $"&$filter={Uri.EscapeDataString(filter)}" +
                      $"&$select=CustomerAccount,PartyNumber,dataAreaId&$top=1";

            var token = await GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[FoCustomerService] Error HTTP {Status} consultando CustomersV3. Body={Body}",
                    httpResponse.StatusCode, content);
                throw new HttpRequestException(
                    $"F&O OData respondio con HTTP {(int)httpResponse.StatusCode}: {content}");
            }

            var page = JsonSerializer.Deserialize<ODataCollection<FoCustomerV3CreatedResponse>>(content);
            return page?.Value is { Count: > 0 } ? page.Value[0] : null;
        }

        // OData escapa la comilla simple duplicandola dentro del literal.
        private static string EscapeODataLiteral(string value) => value.Replace("'", "''");

        private sealed class ODataCollection<T>
        {
            [System.Text.Json.Serialization.JsonPropertyName("value")]
            public List<T>? Value { get; set; }
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
