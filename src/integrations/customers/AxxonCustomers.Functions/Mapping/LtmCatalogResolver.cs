using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>Los dos codigos que salen de la virtual entity de tipos de documento.</summary>
    public sealed record LtmDocType(string? DocTypeId, string? TaxPayerTypeId);

    /// <summary>
    /// Cache de los catalogos, singleton. Va separado del resolver porque el resolver
    /// depende de <see cref="IOrganizationService"/> — mismo criterio que
    /// <see cref="FoSchemaCache"/> y <c>DualWriteCompanyCache</c>.
    /// </summary>
    public sealed class LtmCatalogCache
    {
        internal readonly ConcurrentDictionary<Guid, Lazy<Task<LtmDocType>>> DocTypes = new();

        internal readonly ConcurrentDictionary<string, Lazy<Task<string?>>> AccountTypeGroups =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resuelve los codigos de localizacion que viven en virtual entities de F&amp;O.
    ///
    /// <b>Por que hay cache.</b> Una virtual entity no es una tabla de Dataverse: cada
    /// Retrieve es Dataverse llamando en vivo a la OData de F&amp;O. Sin cache, cada mensaje
    /// sumaria dos viajes al ERP encima de los que ya hace el sync, y las apps que llaman a
    /// F&amp;O corren con <c>maxInstanceCount = 1</c> justamente por sus limites. Los dos
    /// catalogos son chicos y practicamente estaticos (tipos de documento, y grupos de
    /// cliente por legal entity), asi que se resuelven una vez por proceso.
    ///
    /// Un fallo transitorio no deja el catalogo roto para todo el proceso: se descarta la
    /// entrada y el proximo mensaje vuelve a consultar.
    /// </summary>
    public sealed class LtmCatalogResolver
    {
        private readonly IOrganizationService _orgService;
        private readonly LtmCatalogCache _cache;
        private readonly ILogger<LtmCatalogResolver> _logger;

        public LtmCatalogResolver(
            IOrganizationService orgService,
            LtmCatalogCache cache,
            ILogger<LtmCatalogResolver> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _cache      = cache      ?? throw new ArgumentNullException(nameof(cache));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// De la fila de <c>mserp_ltmtaxpayerdoctypeentity</c> que apunta el lookup
        /// <c>axx_tipodocumento</c> del cliente saca los dos codigos que el mapeo necesita
        /// (<c>CountryDocTypeId</c> y <c>TaxPayerTypeId</c>). Los dos salen del mismo
        /// Retrieve: pedirlos por separado seria pagar el viaje dos veces.
        /// </summary>
        public Task<LtmDocType> ResolveDocTypeAsync(Guid virtualRecordId, CancellationToken cancellationToken)
        {
            var entry = _cache.DocTypes.GetOrAdd(virtualRecordId, key =>
                // CancellationToken.None: el resultado se comparte entre mensajes, no puede
                // quedar atado al token del primero que llego.
                new Lazy<Task<LtmDocType>>(() => LoadDocTypeAsync(key, CancellationToken.None)));

            return AwaitOrEvict(entry.Value, cancellationToken, () => _cache.DocTypes.TryRemove(
                new KeyValuePair<Guid, Lazy<Task<LtmDocType>>>(virtualRecordId, entry)));
        }

        /// <summary>
        /// <c>AccountTypeGroupId</c> no se navega desde el cliente: se busca en la virtual
        /// entity la fila de la legal entity cuyo <c>CustVendEntity</c> es Customer. Depende
        /// solo de la company, asi que es una constante por legal entity.
        /// </summary>
        public Task<string?> ResolveAccountTypeGroupAsync(string dataAreaId, CancellationToken cancellationToken)
        {
            var entry = _cache.AccountTypeGroups.GetOrAdd(dataAreaId, key =>
                new Lazy<Task<string?>>(() => LoadAccountTypeGroupAsync(key, CancellationToken.None)));

            return AwaitOrEvict(entry.Value, cancellationToken, () => _cache.AccountTypeGroups.TryRemove(
                new KeyValuePair<string, Lazy<Task<string?>>>(dataAreaId, entry)));
        }

        // ── Carga ─────────────────────────────────────────────────────

        private async Task<LtmDocType> LoadDocTypeAsync(Guid virtualRecordId, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[LtmCatalogResolver] Leyendo {Entity} {RecordId} (virtual entity: va contra F&O).",
                LtmCustMapping.VirtualDocTypeEntity, virtualRecordId);

            var record = await Task.Run(() => _orgService.Retrieve(
                LtmCustMapping.VirtualDocTypeEntity,
                virtualRecordId,
                new ColumnSet(LtmCustMapping.VirtualDocTypeId, LtmCustMapping.VirtualTaxPayerTypeId)),
                cancellationToken);

            var docType = new LtmDocType(
                record.GetAttributeValue<string>(LtmCustMapping.VirtualDocTypeId),
                record.GetAttributeValue<string>(LtmCustMapping.VirtualTaxPayerTypeId));

            _logger.LogInformation(
                "[LtmCatalogResolver] Tipo de documento {RecordId}: DocTypeId={DocTypeId} | " +
                "TaxPayerTypeId={TaxPayerTypeId}",
                virtualRecordId, docType.DocTypeId ?? "null", docType.TaxPayerTypeId ?? "null");

            return docType;
        }

        private async Task<string?> LoadAccountTypeGroupAsync(string dataAreaId, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[LtmCatalogResolver] Buscando el grupo de cliente en {Entity} para {DataAreaId} " +
                "(virtual entity: va contra F&O).",
                LtmCustMapping.VirtualAccountTypeGroupEntity, dataAreaId);

            var query = new QueryExpression(LtmCustMapping.VirtualAccountTypeGroupEntity)
            {
                ColumnSet = new ColumnSet(LtmCustMapping.VirtualAccountTypeGroupId),
                TopCount  = 1,
                Criteria  =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            LtmCustMapping.VirtualAccountTypeGroupCompany,
                            ConditionOperator.Equal,
                            dataAreaId),
                        new ConditionExpression(
                            LtmCustMapping.VirtualAccountTypeGroupCustVend,
                            ConditionOperator.Equal,
                            LtmCustMapping.CustVendEntityCustomer)
                    }
                }
            };

            var result = await Task.Run(() => _orgService.RetrieveMultiple(query), cancellationToken);

            var group = result.Entities.Count > 0
                ? result.Entities[0].GetAttributeValue<string>(LtmCustMapping.VirtualAccountTypeGroupId)
                : null;

            if (string.IsNullOrWhiteSpace(group))
            {
                // No se tira: el campo se omite y F&O aplica su default. Que falte el grupo en
                // una legal entity es configuracion del ERP, no un dato roto del cliente.
                _logger.LogWarning(
                    "[LtmCatalogResolver] No hay grupo de tipo {CustVend} para {DataAreaId} en {Entity}. " +
                    "Se omite {Target} en el payload.",
                    LtmCustMapping.CustVendEntityCustomer, dataAreaId,
                    LtmCustMapping.VirtualAccountTypeGroupEntity, LtmCustMapping.AccountTypeGroupId);
            }
            else
            {
                _logger.LogInformation(
                    "[LtmCatalogResolver] Grupo de cliente de {DataAreaId}: {Group}", dataAreaId, group);
            }

            return group;
        }

        private static async Task<T> AwaitOrEvict<T>(
            Task<T> task,
            CancellationToken cancellationToken,
            Action evict)
        {
            try
            {
                return await task.WaitAsync(cancellationToken);
            }
            catch
            {
                evict();
                throw;
            }
        }
    }
}
