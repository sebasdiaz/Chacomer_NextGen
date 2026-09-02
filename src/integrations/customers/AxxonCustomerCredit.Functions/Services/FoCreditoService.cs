using Axxon.Eip.Core.FinOps;
using AxxonCustomerCredit.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonCustomerCredit.Functions.Services
{
    /// <summary>
    /// Lee las entidades de credito de F&amp;O con el cliente OData generico de la EiP
    /// (cross-company y retry de throttling incluidos).
    ///
    /// Cada metodo arma su <c>$filter</c> con los campos que la entidad realmente tiene:
    /// resoluciones no expone <c>CustomerAccount</c>, asi que ahi no hay filtro por
    /// cuenta ni forma de simularlo sin resolver antes la solicitud.
    /// </summary>
    public class FoCreditoService : IFoCreditoService
    {
        public const string EntitySetClientes     = "DevAxCustCreditCustomers";
        public const string EntitySetPlanes       = "DevAxCustCreditGrantedPlans";
        public const string EntitySetCuotas       = "DevAxCustCreditInstallments";
        public const string EntitySetResoluciones = "DevAxCustCreditResolutions";

        private readonly IFoODataClient _client;
        private readonly ILogger<FoCreditoService> _logger;

        public FoCreditoService(IFoODataClient client, ILogger<FoCreditoService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public Task<CreditoResultado<FoCreditoCliente>> GetClientesAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default) =>
            ReadAsync<FoCreditoCliente>(
                EntitySetClientes,
                Filtro(
                    ("dataAreaId",      consulta.DataAreaId),
                    ("CustomerAccount", consulta.Cuenta)),
                consulta.Top,
                cancellationToken);

        public Task<CreditoResultado<FoCreditoPlan>> GetPlanesAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default) =>
            ReadAsync<FoCreditoPlan>(
                EntitySetPlanes,
                Filtro(
                    ("dataAreaId",      consulta.DataAreaId),
                    ("CustomerAccount", consulta.Cuenta),
                    ("CreditId",        consulta.CreditId),
                    ("RequestId",       consulta.RequestId)),
                consulta.Top,
                cancellationToken);

        public Task<CreditoResultado<FoCreditoCuota>> GetCuotasAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default) =>
            ReadAsync<FoCreditoCuota>(
                EntitySetCuotas,
                Filtro(
                    ("dataAreaId",      consulta.DataAreaId),
                    ("CustomerAccount", consulta.Cuenta),
                    ("CreditId",        consulta.CreditId)),
                consulta.Top,
                cancellationToken);

        public Task<CreditoResultado<FoCreditoResolucion>> GetResolucionesAsync(
            CreditoConsulta consulta, CancellationToken cancellationToken = default) =>
            ReadAsync<FoCreditoResolucion>(
                EntitySetResoluciones,
                Filtro(
                    ("dataAreaId",  consulta.DataAreaId),
                    ("SolicitudId", consulta.SolicitudId)),
                consulta.Top,
                cancellationToken);

        /// <summary>
        /// Arma el <c>$filter</c> con los pares que tienen valor, unidos por <c>and</c>.
        /// Todos los campos filtrables de estas entidades son strings, asi que el literal
        /// va entre comillas y escapado. Sin pares con valor devuelve null: F&amp;O
        /// responde la tabla entera, acotada por el <c>$top</c>.
        /// </summary>
        private static string? Filtro(params (string Campo, string? Valor)[] pares)
        {
            var partes = pares
                .Where(p => !string.IsNullOrWhiteSpace(p.Valor))
                .Select(p => $"{p.Campo} eq '{FoOData.EscapeLiteral(p.Valor!)}'")
                .ToList();

            return partes.Count == 0 ? null : string.Join(" and ", partes);
        }

        /// <summary>
        /// Lee una pagina acotada del entity set.
        ///
        /// Pide <c>top + 1</c> filas para poder decir si quedo algo afuera. Como el
        /// <c>PageSize</c> es ese mismo numero, la fila extra viene en la primera pagina
        /// y el <c>break</c> corta antes de que el cliente pida la siguiente: nunca se
        /// pagina de mas para responder esta pregunta.
        /// </summary>
        private async Task<CreditoResultado<T>> ReadAsync<T>(
            string entitySet,
            string? filter,
            int top,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[FoCreditoService] Leyendo {EntitySet}. top={Top} filter={Filter}",
                entitySet, top, filter ?? "(sin filtro)");

            var query = new FoODataQuery(entitySet)
            {
                // Explicito aunque sea el default de FoODataQuery: sin cross-company F&O
                // devuelve solo la compania default del caller, y un satelite que consulta
                // por CustomerAccount recibiria un subconjunto silencioso de los creditos
                // del cliente. Es el filtro por dataAreaId el que acota, no la falta de
                // este flag.
                CrossCompany = true,
                Filter       = filter,
                PageSize     = top + 1
            };

            var items    = new List<T>(top);
            var truncado = false;

            await foreach (var item in _client.QueryAsync<T>(query, cancellationToken))
            {
                if (items.Count == top)
                {
                    truncado = true;
                    break;
                }

                items.Add(item);
            }

            if (truncado)
                _logger.LogWarning(
                    "[FoCreditoService] {EntitySet} tiene mas de {Top} filas para este " +
                    "filtro: la respuesta va truncada. filter={Filter}",
                    entitySet, top, filter ?? "(sin filtro)");

            return new CreditoResultado<T>(items, truncado);
        }
    }
}
