using System.Collections.Concurrent;
using System.Text.Json;
using Axxon.Eip.Core.FinOps;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>Resuelve el casing exacto de las propiedades OData de un entity set de F&amp;O.</summary>
    public interface IFoSchemaProvider
    {
        /// <summary>
        /// Devuelve el nombre de la propiedad tal cual lo espera la API, o null si el
        /// entity set no tiene ninguna propiedad con ese nombre.
        /// </summary>
        Task<string?> ResolvePropertyAsync(string entitySet, string name, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Cache del sondeo, singleton. Va separado del provider porque el provider depende
    /// de IFoODataClient (transient, con un HttpClient de IHttpClientFactory que no
    /// conviene retener): asi el cache sobrevive y el HttpClient no queda capturado.
    /// </summary>
    public sealed class FoSchemaCache
    {
        internal readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, string>>>> Entries =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El export de Dual Write escribe los campos de F&amp;O en MAYUSCULAS
    /// ("PARTYNUMBER"), que son los nombres de la tabla de AX. La API OData es
    /// case-sensitive y espera "PartyNumber" — y no siempre es un PascalCase derivable:
    /// la propiedad es "A365Sellable", no "A365SELLABLE" (ver Models/FoCustomerV3.cs).
    /// Por eso el casing se resuelve contra el environment y no por algoritmo.
    ///
    /// El sondeo es un GET de un registro del entity set: la respuesta trae todas las
    /// propiedades con su nombre exacto. Se hace una vez por entity set y queda cacheado
    /// mientras viva el proceso.
    ///
    /// Se resuelve en el primer mensaje y no al arranque a proposito: no queremos que el
    /// host quede colgado de la disponibilidad de F&amp;O en cada cold start.
    /// </summary>
    public sealed class FoSchemaProvider : IFoSchemaProvider
    {
        private readonly IFoODataClient _client;
        private readonly FoSchemaCache _cache;
        private readonly ILogger<FoSchemaProvider> _logger;

        public FoSchemaProvider(IFoODataClient client, FoSchemaCache cache, ILogger<FoSchemaProvider> logger)
        {
            _client = client;
            _cache  = cache;
            _logger = logger;
        }

        public async Task<string?> ResolvePropertyAsync(
            string entitySet,
            string name,
            CancellationToken cancellationToken = default)
        {
            var entry = _cache.Entries.GetOrAdd(entitySet, key =>
                new Lazy<Task<IReadOnlyDictionary<string, string>>>(
                    // CancellationToken.None: el sondeo se comparte entre mensajes, no
                    // puede quedar atado al token del primero que llego.
                    () => LoadPropertiesAsync(key, CancellationToken.None)));

            IReadOnlyDictionary<string, string> properties;

            try
            {
                properties = await entry.Value.WaitAsync(cancellationToken);
            }
            catch
            {
                // Un fallo transitorio de F&O no puede dejar el casing roto para todo el
                // proceso: se descarta el intento y el proximo mensaje vuelve a sondear.
                _cache.Entries.TryRemove(
                    new KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<string, string>>>>(entitySet, entry));
                throw;
            }

            // Sin sondeo (entity set vacio) se manda el nombre tal cual y que F&O opine.
            if (properties.Count == 0)
                return name;

            return properties.TryGetValue(name, out var exact) ? exact : null;
        }

        private async Task<IReadOnlyDictionary<string, string>> LoadPropertiesAsync(
            string entitySet,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[FoSchemaProvider] Sondeando {EntitySet} para resolver el casing de las propiedades OData.",
                entitySet);

            var sample = await _client.FindFirstAsync<Dictionary<string, JsonElement>>(
                new FoODataQuery(entitySet), cancellationToken);

            if (sample is null || sample.Count == 0)
            {
                _logger.LogWarning(
                    "[FoSchemaProvider] {EntitySet} no devolvio ningun registro: no se puede resolver el " +
                    "casing. Los campos se mandan tal cual estan en el mapeo.",
                    entitySet);

                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in sample.Keys)
            {
                // Anotaciones OData (@odata.etag y demas) no son campos de la entidad.
                if (property.Contains('@'))
                    continue;

                properties[property] = property;
            }

            _logger.LogInformation(
                "[FoSchemaProvider] {EntitySet}: {Count} propiedades resueltas.",
                entitySet, properties.Count);

            return properties;
        }
    }
}
