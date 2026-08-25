using AxxonCustomers.Functions.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomers.Functions.Services
{
    /// <summary>Resultado de una corrida de backfill.</summary>
    public sealed record LtmBackfillResult(
        string Entity,
        bool DryRun,
        int Candidates,
        int Enqueued,
        int Pages);

    public interface ILtmCustBackfillService
    {
        Task<LtmBackfillResult> RunAsync(
            LtmCustSource source,
            bool dryRun,
            int? max,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Encola los clientes que ya existen en F&amp;O para que se les escriba la fila de
    /// <c>LTMCustTable</c>. Es el proceso de <b>una sola vez</b> que cubre lo que se creo
    /// antes de que existiera esta integracion.
    ///
    /// <b>Encola, no procesa.</b> Podria llamar directo a <see cref="ILtmCustSyncService"/>
    /// en un loop, pero entonces el backfill entero viviria dentro de una ejecucion: se
    /// come el timeout con volumen alto y un fallo a la mitad deja el trabajo por la mitad
    /// sin registro de donde quedo. Publicando en la cola, cada cliente es un mensaje
    /// independiente con el retry, el DLQ y el orden que <c>LtmCustSyncFunction</c> ya tiene.
    ///
    /// <b>No es idempotente</b>, y no puede serlo: la v1 escribe con un POST y sin consultar
    /// si la fila existe, asi que correr el backfill dos veces manda al DLQ todo lo que ya
    /// se escribio. Por eso lo dispara un HTTP trigger y no un timer — ver
    /// <c>Functions/LtmCustBackfillFunction.cs</c>.
    /// </summary>
    public class LtmCustBackfillService : ILtmCustBackfillService
    {
        /// <summary>Tamanio de pagina del Retrieve. El maximo que acepta Dataverse es 5000.</summary>
        private const int PageSize = 500;

        /// <summary>Flag de master. Mismo logical name en contact y en account.</summary>
        private const string IsMasterAttribute = "axx_ismaster";

        private readonly IOrganizationService _orgService;
        private readonly LtmSyncDispatcher _dispatcher;
        private readonly ILogger<LtmCustBackfillService> _logger;

        public LtmCustBackfillService(
            IOrganizationService orgService,
            LtmSyncDispatcher dispatcher,
            ILogger<LtmCustBackfillService> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<LtmBackfillResult> RunAsync(
            LtmCustSource source,
            bool dryRun,
            int? max,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[LtmCustBackfillService] Inicio. Entidad={Entity} | DryRun={DryRun} | Max={Max}",
                source.EntityLogicalName, dryRun, max?.ToString() ?? "sin limite");

            var query = BuildQuery(source);
            var paging = new PagingInfo { Count = PageSize, PageNumber = 1 };

            var candidates = 0;
            var enqueued   = 0;
            var pages      = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                query.PageInfo = paging;

                var page = _orgService.RetrieveMultiple(query);
                pages++;

                foreach (var record in page.Entities)
                {
                    if (max is not null && candidates >= max)
                    {
                        _logger.LogInformation(
                            "[LtmCustBackfillService] Corte por el limite pedido ({Max}). " +
                            "Quedan candidatos sin encolar.", max);

                        return Done(source, dryRun, candidates, enqueued, pages);
                    }

                    candidates++;

                    if (dryRun)
                        continue;

                    await _dispatcher.DispatchAsync(
                        source.EntityLogicalName,
                        record.Id,
                        DataAreaIdOf(record),
                        cancellationToken);

                    enqueued++;
                }

                if (!page.MoreRecords)
                    break;

                paging.PageNumber++;
                paging.PagingCookie = page.PagingCookie;
            }

            return Done(source, dryRun, candidates, enqueued, pages);
        }

        /// <summary>
        /// Los candidatos son los que <b>ya tienen customer en F&amp;O</b>: el campo de
        /// write-back con valor es exactamente esa senial.
        ///
        /// Se filtran ademas los dos casos que sabemos que terminarian en el DLQ, para no
        /// ensuciarlo con registros que nunca iban a andar:
        ///   - los master, que son registros de consolidacion y no van al ERP;
        ///   - los que no tienen legal entity, sin la cual no hay dataAreaId.
        /// </summary>
        private static QueryExpression BuildQuery(LtmCustSource source)
        {
            var query = new QueryExpression(source.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(LtmCustMapping.CompanyAttribute),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(source.AccountNumberAttribute, ConditionOperator.NotNull),
                        new ConditionExpression(LtmCustMapping.CompanyAttribute, ConditionOperator.NotNull)
                    }
                }
            };

            // Un Yes/No que nunca se seteo no viene como false: viene sin valor. Pedir
            // "distinto de true" dejaria afuera justamente a los que nadie marco nunca, que
            // son la mayoria de los raws.
            var notMaster = new FilterExpression(LogicalOperator.Or);
            notMaster.AddCondition(IsMasterAttribute, ConditionOperator.Null);
            notMaster.AddCondition(IsMasterAttribute, ConditionOperator.Equal, false);
            query.Criteria.AddFilter(notMaster);

            return query;
        }

        /// <summary>
        /// El <c>dataAreaId</c> del mensaje es solo traza: el consumidor lo resuelve al releer
        /// el registro. Aca se usa el nombre del lookup a la company, que viene sin costo en el
        /// mismo Retrieve; si no resolvio, viaja null y no pasa nada.
        /// </summary>
        private static string? DataAreaIdOf(Entity record) =>
            record.GetAttributeValue<EntityReference>(LtmCustMapping.CompanyAttribute)?.Name;

        private LtmBackfillResult Done(
            LtmCustSource source, bool dryRun, int candidates, int enqueued, int pages)
        {
            _logger.LogInformation(
                "[LtmCustBackfillService] Fin. Entidad={Entity} | DryRun={DryRun} | " +
                "Candidatos={Candidates} | Encolados={Enqueued} | Paginas={Pages}",
                source.EntityLogicalName, dryRun, candidates, enqueued, pages);

            return new LtmBackfillResult(source.EntityLogicalName, dryRun, candidates, enqueued, pages);
        }
    }
}
