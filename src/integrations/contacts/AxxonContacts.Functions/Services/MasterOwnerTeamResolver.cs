using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonContacts.Functions.Services
{
    /// <summary>
    /// Cache del equipo dueño de los masters. Singleton y separado del resolver porque el
    /// resolver depende de IOrganizationService, que en esta app es transient (uno por
    /// invocacion). Mismo criterio que DualWriteCompanyCache.
    ///
    /// Sin TTL, a diferencia de aquella: el equipo es configuracion del ambiente, no un
    /// flag que un admin cambia para redirigir trafico. Cambiar el app setting recicla la
    /// app, y con eso se vacia la cache.
    /// </summary>
    public sealed class MasterOwnerTeamCache
    {
        internal volatile EntityReference? Team;
    }

    /// <summary>
    /// Resuelve el owner team al que se asignan los masters — el "cliente unico".
    ///
    /// El app setting <c>MasterOwnerTeamName</c> lleva el NOMBRE y no el id porque el GUID
    /// del equipo es distinto en cada environment y el nombre es el mismo, asi que un
    /// unico valor sirve para todos los ambientes. El default team de una business unit se
    /// llama igual que la BU, por lo que 'CLIENTE UNICO' resuelve el equipo de la business
    /// unit CLIENTE UNICO sin tener que crear un equipo aparte.
    ///
    /// Sin el setting no se asigna owner y el master queda del usuario con el que corre la
    /// app, que es como venia funcionando.
    ///
    /// <b>Si el setting esta y el equipo no aparece, lanza.</b> El mensaje se reintenta y,
    /// si el problema persiste, cae al DLQ. Es a proposito: un master creado en la business
    /// unit equivocada no falla en ningun lado, queda visible para quien no corresponde y
    /// hay que reasignarlo a mano despues.
    /// </summary>
    public sealed class MasterOwnerTeamResolver
    {
        public const string EntityLogicalName = "team";

        /// <summary>teamtype 0 = Owner. Solo un owner team puede ser dueño de un registro.</summary>
        private const int OwnerTeamType = 0;

        private readonly IOrganizationService _service;
        private readonly MasterOwnerTeamCache _cache;
        private readonly string?              _teamName;
        private readonly ILogger              _logger;

        public MasterOwnerTeamResolver(
            IOrganizationService service,
            MasterOwnerTeamCache cache,
            string? teamName,
            ILogger logger)
        {
            _service  = service ?? throw new ArgumentNullException(nameof(service));
            _cache    = cache   ?? throw new ArgumentNullException(nameof(cache));
            _teamName = teamName;
            _logger   = logger  ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Referencia al equipo dueño, o null si no hay equipo configurado (el master se
        /// crea con el owner por defecto).
        /// </summary>
        public async Task<EntityReference?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_teamName)) return null;

            var cached = _cache.Team;
            if (cached != null) return cached;

            // Sin lock: dos invocaciones concurrentes pueden resolver el mismo equipo dos
            // veces y escribir el mismo valor. Se paga un Retrieve de mas, no un bug.
            var team = await Task.Run(Load, cancellationToken);

            _cache.Team = team;
            return team;
        }

        private EntityReference Load()
        {
            var query = new QueryExpression(EntityLogicalName)
            {
                ColumnSet = new ColumnSet("name"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("name", ConditionOperator.Equal, _teamName),
                        new ConditionExpression("teamtype", ConditionOperator.Equal, OwnerTeamType)
                    }
                },
                TopCount = 2
            };

            var results = _service.RetrieveMultiple(query);

            if (results.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"No existe un owner team llamado '{_teamName}' en Dataverse. Es el valor del " +
                    "app setting 'MasterOwnerTeamName': corregilo, o vacialo para que los masters " +
                    "se creen con el owner por defecto.");

            if (results.Entities.Count > 1)
                _logger.LogWarning(
                    "[MasterOwnerTeamResolver] {Count} owner teams llamados '{TeamName}'. Usando el primero ({TeamId}).",
                    results.Entities.Count, _teamName, results.Entities[0].Id);

            var team = results.Entities[0].ToEntityReference();

            _logger.LogInformation(
                "[MasterOwnerTeamResolver] Los masters se asignan al equipo '{TeamName}' ({TeamId}).",
                _teamName, team.Id);

            return team;
        }
    }
}
