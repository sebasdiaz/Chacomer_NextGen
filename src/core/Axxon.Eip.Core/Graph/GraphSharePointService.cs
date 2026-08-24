using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Axxon.Eip.Core.Graph
{
    /// <inheritdoc cref="IGraphSharePointService"/>
    public sealed class GraphSharePointService : IGraphSharePointService
    {
        /// <summary>Nombre del HttpClient en IHttpClientFactory.</summary>
        public const string HttpClientName = "MicrosoftGraph";

        public const string BaseAddress = "https://graph.microsoft.com/v1.0/";

        private static readonly string[] Scopes = { "https://graph.microsoft.com/.default" };

        // IHttpClientFactory y no un HttpClient capturado: el servicio es singleton (cachea
        // el site id) y aferrarse a una instancia saltea la rotacion de handlers.
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TokenCredential _credential;
        private readonly GraphOptions _options;
        private readonly ILogger<GraphSharePointService> _logger;

        // El site id no cambia: se resuelve una vez por instancia. El gate evita que N
        // requests concurrentes disparen N llamadas a Graph durante el arranque en frio.
        private readonly SemaphoreSlim _siteIdGate = new(1, 1);
        private string? _siteId;

        public GraphSharePointService(
            IHttpClientFactory httpClientFactory,
            TokenCredential credential,
            GraphOptions options,
            ILogger<GraphSharePointService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _credential        = credential;
            _options           = options;
            _logger            = logger;
        }

        public async Task<string> GetSiteIdAsync(CancellationToken cancellationToken = default)
        {
            if (_siteId is not null)
                return _siteId;

            await _siteIdGate.WaitAsync(cancellationToken);
            try
            {
                if (_siteId is not null)
                    return _siteId;

                if (string.IsNullOrWhiteSpace(_options.SharePointSiteUrl))
                    throw new InvalidOperationException(
                        "'SharePointSiteUrl' no esta configurado. Sin el no se puede resolver el " +
                        "sitio de SharePoint contra el que operar.");

                if (!Uri.TryCreate(_options.SharePointSiteUrl, UriKind.Absolute, out var siteUri))
                    throw new InvalidOperationException(
                        $"'SharePointSiteUrl' no es una URL absoluta valida: {_options.SharePointSiteUrl}");

                // sites/{hostname}:{server-relative-path} -> el objeto site, con su id compuesto.
                var path = siteUri.AbsolutePath.TrimEnd('/');
                var json = await GetJsonAsync($"sites/{siteUri.Host}:{path}", "GetSite", cancellationToken);

                _siteId = json.TryGetProperty("id", out var id) ? id.GetString() : null;

                if (string.IsNullOrWhiteSpace(_siteId))
                    throw new GraphException("GetSite", HttpStatusCode.OK,
                        "La respuesta del sitio no trae 'id'.");

                _logger.LogInformation("[Graph] Site resuelto: {SiteUrl}", _options.SharePointSiteUrl);
                return _siteId;
            }
            finally
            {
                _siteIdGate.Release();
            }
        }

        public async Task<GraphDriveItem> UploadAsync(
            string drivePath, byte[] content, string contentType, CancellationToken cancellationToken = default)
        {
            var siteId = await GetSiteIdAsync(cancellationToken);
            var url    = $"sites/{siteId}/drive/root:/{EscapePath(drivePath)}:/content";

            using var body = new ByteArrayContent(content);
            body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await SendAsync(HttpMethod.Put, url, body, cancellationToken);
            var json = await ReadJsonAsync(response, "Upload", cancellationToken);

            return new GraphDriveItem(
                json.GetProperty("id").GetString() ?? string.Empty,
                json.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                json.TryGetProperty("webUrl", out var w) ? w.GetString() ?? string.Empty : string.Empty);
        }

        public async Task<byte[]> DownloadAsPdfAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var siteId = await GetSiteIdAsync(cancellationToken);
            var url    = $"sites/{siteId}/drive/items/{itemId}/content?format=pdf";

            using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new GraphException("ConvertToPdf", response.StatusCode,
                    await response.Content.ReadAsStringAsync(cancellationToken));

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        public async Task DeleteAsync(string itemId, CancellationToken cancellationToken = default)
        {
            var siteId = await GetSiteIdAsync(cancellationToken);

            using var response = await SendAsync(
                HttpMethod.Delete, $"sites/{siteId}/drive/items/{itemId}", content: null, cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return;

            throw new GraphException("Delete", response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }

        public async Task EnsureFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            var siteId   = await GetSiteIdAsync(cancellationToken);
            var segments = folderPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var parent   = string.Empty;

            foreach (var segment in segments)
            {
                // La raiz se direcciona distinto que una subcarpeta: root/children vs root:/{path}:/children.
                var url = string.IsNullOrEmpty(parent)
                    ? $"sites/{siteId}/drive/root/children"
                    : $"sites/{siteId}/drive/root:/{EscapePath(parent)}:/children";

                using var body = JsonContent.Create(new Dictionary<string, object?>
                {
                    ["name"]   = segment,
                    ["folder"] = new Dictionary<string, object?>(),
                    // "fail" y no "replace": replace borraria el contenido de una carpeta
                    // que ya existe. El 409 resultante es la senal de que ya estaba.
                    ["@microsoft.graph.conflictBehavior"] = "fail"
                });

                using var response = await SendAsync(HttpMethod.Post, url, body, cancellationToken);

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
                    throw new GraphException("CreateFolder", response.StatusCode,
                        await response.Content.ReadAsStringAsync(cancellationToken));

                parent = string.IsNullOrEmpty(parent) ? segment : $"{parent}/{segment}";
            }
        }

        public async Task<byte[]> ConvertToPdfAsync(
            byte[] officeDocument,
            string tempFolder,
            string fileExtension,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            // Nombre unico por conversion: dos usuarios sobre el mismo registro colisionan
            // si el nombre depende solo del id, y el DELETE de uno le borra el temporal al otro.
            var tempName = $"{Guid.NewGuid():N}{fileExtension}";
            var tempPath = $"{tempFolder.Trim('/')}/{tempName}";

            GraphDriveItem? uploaded = null;
            try
            {
                await EnsureFolderAsync(tempFolder, cancellationToken);
                uploaded = await UploadAsync(tempPath, officeDocument, contentType, cancellationToken);
                return await DownloadAsPdfAsync(uploaded.Id, cancellationToken);
            }
            finally
            {
                if (uploaded is not null)
                {
                    try
                    {
                        // El temporal se borra siempre, incluso si la conversion fallo.
                        await DeleteAsync(uploaded.Id, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // Un temporal huerfano no justifica tumbar la operacion: se loguea y sigue.
                        _logger.LogWarning(ex,
                            "[Graph] No se pudo borrar el temporal {Path}.", tempPath);
                    }
                }
            }
        }

        // -- Helpers HTTP -------------------------------------------------

        private async Task<JsonElement> GetJsonAsync(
            string url, string operation, CancellationToken cancellationToken)
        {
            using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
            return await ReadJsonAsync(response, operation, cancellationToken);
        }

        private static async Task<JsonElement> ReadJsonAsync(
            HttpResponseMessage response, string operation, CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new GraphException(operation, response.StatusCode, body);

            return JsonSerializer.Deserialize<JsonElement>(body);
        }

        // El token va por request y no en DefaultRequestHeaders: el HttpClient es compartido.
        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(Scopes), cancellationToken);

            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            if (content is not null)
                request.Content = content;

            var http = _httpClientFactory.CreateClient(HttpClientName);
            return await http.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Escapa cada segmento de la ruta dejando las barras: Graph usa el path crudo
        /// entre <c>root:/</c> y <c>:/content</c>, asi que espacios y acentos rompen la URL.
        /// </summary>
        private static string EscapePath(string drivePath) =>
            string.Join('/', drivePath.Trim('/').Split('/').Select(Uri.EscapeDataString));
    }
}
