using Axxon.Eip.Core.Dataverse;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>Lo que se sabe de un template segun axx_metatemplates.</summary>
    /// <param name="Found">false si el axx_id no esta en la tabla.</param>
    /// <param name="Name">axx_name, para los logs y los mensajes de error.</param>
    /// <param name="Status">axx_status tal como lo manda Thinkchat (APPROVED, ARCHIVED…).</param>
    /// <param name="Type">axx_type: text, image o video. Los dos ultimos exigen template_media.</param>
    /// <param name="Active">statecode = Active.</param>
    /// <param name="Variables">
    /// Cantidad de parametros posicionales que espera la plantilla. null cuando
    /// axx_variables no es un numero — ahi no se puede validar la cantidad.
    /// </param>
    public record MetatemplateInfo(
        bool Found,
        string? Name,
        string? Status,
        string? Type,
        bool Active,
        int? Variables)
    {
        public static readonly MetatemplateInfo NotFound = new(false, null, null, null, false, null);

        /// <summary>
        /// true si la plantilla tiene header de media y por lo tanto exige
        /// template_media. Se decide por exclusion —cualquier type que no sea "text"—
        /// porque hoy se ven "image" y "video" pero Meta soporta mas (document,
        /// location) y no hay motivo para que un type nuevo pase de largo.
        /// </summary>
        public bool RequiresMedia =>
            !string.IsNullOrWhiteSpace(Type)
            && !Type.Equals("text", StringComparison.OrdinalIgnoreCase);
    }

    public interface IMetatemplateLookup
    {
        MetatemplateInfo Lookup(string templateId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Consulta axx_metatemplates para validar un envio antes de gastarlo.
    ///
    /// Se registra como **singleton** a proposito. DataverseClientFactory es transient y
    /// cada resolucion arma un ServiceClient nuevo, con su handshake de ~1s: aceptable
    /// para un timer que corre cada dos horas, no para un endpoint HTTP que paga ese
    /// costo en cada request. El ServiceClient es thread-safe, asi que se crea uno solo,
    /// perezosamente, en el primer envio.
    /// </summary>
    public class MetatemplateLookup : IMetatemplateLookup
    {
        private const string EntityName = "axx_metatemplates";
        private const string IdField    = "axx_id";
        private const int    StateActive = 0;

        private readonly Lazy<IOrganizationService> _service;
        private readonly ILogger<MetatemplateLookup> _logger;

        public MetatemplateLookup(
            Func<IOrganizationService> serviceFactory,
            ILogger<MetatemplateLookup> logger)
        {
            _logger  = logger;
            _service = new Lazy<IOrganizationService>(
                serviceFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public MetatemplateInfo Lookup(string templateId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(
                    "axx_name", "axx_status", "axx_type", "axx_variables", "statecode"),
                NoLock    = true,
                TopCount  = 1
            };
            query.Criteria.AddCondition(IdField, ConditionOperator.Equal, templateId.Trim());

            // RetrieveMultiple y no Retrieve por alternate key: el "no existe" vuelve como
            // coleccion vacia en vez de una FaultException, que es lo habitual aca —
            // el template puede no haberse sincronizado todavia.
            var result = _service.Value.RetrieveMultiple(query);

            if (result.Entities.Count == 0)
                return MetatemplateInfo.NotFound;

            var e = result.Entities[0];

            return new MetatemplateInfo(
                Found:     true,
                Name:      e.GetAttributeValue<string>("axx_name"),
                Status:    e.GetAttributeValue<string>("axx_status"),
                Type:      e.GetAttributeValue<string>("axx_type"),
                Active:    e.GetAttributeValue<OptionSetValue>("statecode")?.Value == StateActive,
                Variables: ParseVariables(e.GetAttributeValue<string>("axx_variables"), templateId));
        }

        /// <summary>
        /// axx_variables guarda el JSON crudo de lo que manda Thinkchat, que hoy es un
        /// numero ("0", "1", "2" — verificado contra los 112 templates de INTE, donde
        /// coincide con el maximo {{n}} del texto). Si algun dia manda otra cosa, se
        /// devuelve null y la validacion de cantidad se saltea en vez de bloquear un
        /// envio legitimo.
        /// </summary>
        private int? ParseVariables(string? raw, string templateId)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            if (int.TryParse(raw.Trim(), out var count) && count >= 0)
                return count;

            _logger.LogWarning(
                "[MetatemplateLookup] axx_variables no es un numero (TemplateId={TemplateId} Valor={Valor}). " +
                "No se valida la cantidad de parametros.",
                templateId, raw);

            return null;
        }
    }
}
