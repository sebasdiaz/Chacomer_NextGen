using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// Carga y compila los mapeos de la carpeta Mappings al arranque.
    ///
    /// Convencion de nombres: <c>{entitySet}.{mapa}.dualwrite.json</c> (el export, intacto)
    /// mas <c>{entitySet}.{mapa}.overlay.json</c> (lo nuestro). El nombre del mapa es el
    /// que se usa para pedirlo con <see cref="Get"/>.
    ///
    /// Si algun mapeo no compila, la excepcion sube y el host no levanta. Es a proposito.
    /// </summary>
    public sealed class EntityMapRegistry
    {
        public const string DefaultDirectory = "Mappings";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            // Los overlays llevan comentarios: sin el "por que" de cada correccion son
            // imposibles de revisar en un PR.
            ReadCommentHandling         = JsonCommentHandling.Skip
        };

        private readonly IReadOnlyDictionary<string, EntityMap> _maps;

        private EntityMapRegistry(IReadOnlyDictionary<string, EntityMap> maps) => _maps = maps;

        public IEnumerable<EntityMap> All => _maps.Values;

        public EntityMap Get(string name) =>
            _maps.TryGetValue(name, out var map)
                ? map
                : throw new InvalidOperationException(
                      $"No hay ningun mapeo cargado con el nombre '{name}'. " +
                      $"Cargados: {string.Join(", ", _maps.Keys)}.");

        public static EntityMapRegistry Load(string directory, ILogger logger)
        {
            // El working directory del host de Functions no es el de los binarios.
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(AppContext.BaseDirectory, directory);

            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException(
                    $"No existe la carpeta de mapeos '{Path.GetFullPath(directory)}'. " +
                    "Revisar que los archivos esten marcados como Content en el csproj.");

            var maps = new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase);

            foreach (var overlayPath in Directory.EnumerateFiles(directory, "*.overlay.json").OrderBy(p => p))
            {
                var name        = MapNameFrom(overlayPath);
                var exportPath  = overlayPath.Replace(".overlay.json", ".dualwrite.json", StringComparison.OrdinalIgnoreCase);

                if (!File.Exists(exportPath))
                    throw new FileNotFoundException(
                        $"El overlay '{Path.GetFileName(overlayPath)}' no tiene su export de Dual Write " +
                        $"'{Path.GetFileName(exportPath)}'.");

                var overlay = Deserialize<MappingOverlayDocument>(overlayPath);
                var export  = Deserialize<DualWriteMapDocument>(exportPath);

                if (!string.Equals(overlay.Target.SourceEntity, name, StringComparison.OrdinalIgnoreCase))
                    throw new MappingCompilationException(name, new[]
                    {
                        $"target.sourceEntity es '{overlay.Target.SourceEntity}' pero el archivo se llama " +
                        $"'{Path.GetFileName(overlayPath)}'. Tienen que coincidir."
                    });

                var map = EntityMapCompiler.Compile(name, export, overlay);
                maps[name] = map;

                logger.LogInformation(
                    "[EntityMapRegistry] Mapeo '{Name}' compilado: {FieldCount} campos hacia {EntitySet}, " +
                    "write-back en {WriteBack}, {ConditionCount} condicion(es) de sync.",
                    name, map.Fields.Count, map.EntitySet, map.WriteBackAttribute, map.SyncWhen.Count);
            }

            if (maps.Count == 0)
                throw new InvalidOperationException(
                    $"No se encontro ningun archivo *.overlay.json en '{Path.GetFullPath(directory)}'.");

            return new EntityMapRegistry(maps);
        }

        /// <summary>"customersv3.contact.overlay.json" -&gt; "contact".</summary>
        private static string MapNameFrom(string overlayPath)
        {
            var fileName = Path.GetFileName(overlayPath);
            var stem     = fileName[..^".overlay.json".Length];
            var lastDot  = stem.LastIndexOf('.');

            return lastDot >= 0 ? stem[(lastDot + 1)..] : stem;
        }

        private static T Deserialize<T>(string path)
        {
            using var stream = File.OpenRead(path);

            try
            {
                return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                       ?? throw new InvalidOperationException($"'{Path.GetFileName(path)}' esta vacio.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' no es JSON valido: {ex.Message}", ex);
            }
        }
    }
}
