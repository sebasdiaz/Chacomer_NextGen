using System.Collections.Concurrent;
using Axxon.Eip.Core.FinOps;
using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>
    /// La configuracion de localizacion PY de una legal entity, tal como la tiene el ERP.
    /// </summary>
    /// <param name="RucTaxPayerTypes">
    /// Tipos de contribuyente que la company tiene dados de alta para documento RUC
    /// (tipicamente <c>PN</c> y <c>PJ</c>).
    /// </param>
    /// <param name="AccountTypeGroupId">
    /// Grupo de cliente de la localizacion, o null si la company no tiene ninguno o tiene
    /// mas de uno (ver <see cref="LtmCatalogResolver"/>).
    /// </param>
    public sealed record LtmCompanyLocalization(
        IReadOnlyCollection<string> RucTaxPayerTypes,
        string? AccountTypeGroupId)
    {
        /// <summary>
        /// La company tiene la localizacion PY configurada. Es la guarda de alcance: sin
        /// filas de documento no hay nada que escribir en <c>LTMCustTable</c>.
        /// </summary>
        public bool IsConfigured => RucTaxPayerTypes.Count > 0;

        public static readonly LtmCompanyLocalization NotConfigured = new([], null);
    }

    /// <summary>
    /// Cache de los catalogos, singleton. Va separado del resolver porque el resolver
    /// depende de <see cref="IOrganizationService"/> — mismo criterio que
    /// <see cref="FoSchemaCache"/> y <c>DualWriteCompanyCache</c>.
    /// </summary>
    public sealed class LtmCatalogCache
    {
        internal readonly ConcurrentDictionary<string, Lazy<Task<LtmCompanyLocalization>>> Companies =
            new(StringComparer.OrdinalIgnoreCase);

        internal readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, string>>>> States =
            new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resuelve los catalogos de la localizacion: los de Dataverse (virtual entities de
    /// F&amp;O) y el de estados, que se lee por la OData del ERP.
    ///
    /// <b>Por que hay cache.</b> Una virtual entity no es una tabla de Dataverse: cada
    /// Retrieve es Dataverse llamando en vivo a la OData de F&amp;O. Sin cache, cada mensaje
    /// sumaria viajes al ERP encima de los que ya hace el sync, y las apps que llaman a
    /// F&amp;O corren con <c>maxInstanceCount = 1</c> justamente por sus limites. Los
    /// catalogos son chicos y practicamente estaticos (documentos y grupos por legal entity,
    /// departamentos de Paraguay), asi que se resuelven una vez por proceso.
    ///
    /// Un fallo transitorio no deja el catalogo roto para todo el proceso: se descarta la
    /// entrada y el proximo mensaje vuelve a consultar.
    /// </summary>
    public sealed class LtmCatalogResolver
    {
        private readonly IOrganizationService _orgService;
        private readonly IFoODataClient _foClient;
        private readonly LtmCatalogCache _cache;
        private readonly ILogger<LtmCatalogResolver> _logger;

        public LtmCatalogResolver(
            IOrganizationService orgService,
            IFoODataClient foClient,
            LtmCatalogCache cache,
            ILogger<LtmCatalogResolver> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _foClient   = foClient   ?? throw new ArgumentNullException(nameof(foClient));
            _cache      = cache      ?? throw new ArgumentNullException(nameof(cache));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Lo que el ERP tiene configurado de la localizacion PY para una legal entity: los
        /// tipos de contribuyente con documento RUC y el grupo de cliente. Depende solo de la
        /// company, asi que es una constante por legal entity.
        /// </summary>
        public Task<LtmCompanyLocalization> ResolveCompanyAsync(
            string dataAreaId,
            CancellationToken cancellationToken)
        {
            var entry = _cache.Companies.GetOrAdd(dataAreaId, key =>
                // CancellationToken.None: el resultado se comparte entre mensajes, no puede
                // quedar atado al token del primero que llego.
                new Lazy<Task<LtmCompanyLocalization>>(() => LoadCompanyAsync(key, CancellationToken.None)));

            return AwaitOrEvict(entry.Value, cancellationToken, () => _cache.Companies.TryRemove(
                new KeyValuePair<string, Lazy<Task<LtmCompanyLocalization>>>(dataAreaId, entry)));
        }

        /// <summary>
        /// Devuelve el codigo de estado tal como lo escribe F&amp;O, o null si el valor que
        /// trae CRM no existe en el catalogo del pais.
        ///
        /// <b>Por que se valida en vez de pasar el dato tal cual.</b> El
        /// <c>stateorprovince</c> de Dataverse es texto libre y esta sucio (conviven
        /// <c>DPTO_11</c>, <c>Central</c>, <c>CEN</c> y <c>BA</c>), y un estado que F&amp;O no
        /// conoce se rechaza con un 400 que manda al DLQ la fila entera — por un campo que no
        /// es el objetivo de la integracion. Se devuelve la grafia del ERP y no la de CRM
        /// porque la comparacion es case-insensitive y el destino no.
        /// </summary>
        public async Task<string?> ResolveStateAsync(
            string countryRegionId,
            string candidate,
            CancellationToken cancellationToken)
        {
            var entry = _cache.States.GetOrAdd(countryRegionId, key =>
                new Lazy<Task<IReadOnlyDictionary<string, string>>>(
                    () => LoadStatesAsync(key, CancellationToken.None)));

            var states = await AwaitOrEvict(entry.Value, cancellationToken, () => _cache.States.TryRemove(
                new KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<string, string>>>>(countryRegionId, entry)));

            return states.TryGetValue(candidate.Trim(), out var state) ? state : null;
        }

        // ── Carga ─────────────────────────────────────────────────────

        private async Task<LtmCompanyLocalization> LoadCompanyAsync(
            string dataAreaId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[LtmCatalogResolver] Leyendo la configuracion de localizacion de {DataAreaId} " +
                "(virtual entities: van contra F&O).",
                dataAreaId);

            var taxPayerTypes = await LoadRucTaxPayerTypesAsync(dataAreaId, cancellationToken);

            if (taxPayerTypes.Count == 0)
            {
                // No es un error: son las legal entities fuera del alcance PY. Que no tengan
                // filas de documento es exactamente lo que las distingue.
                _logger.LogInformation(
                    "[LtmCatalogResolver] {DataAreaId} no tiene filas de documento {DocType} en " +
                    "{Entity}: la legal entity no tiene la localizacion PY configurada.",
                    dataAreaId, LtmCustMapping.CountryDocTypeRuc, LtmCustMapping.VirtualDocTypeEntity);

                return LtmCompanyLocalization.NotConfigured;
            }

            var accountTypeGroup = await LoadAccountTypeGroupAsync(dataAreaId, cancellationToken);

            _logger.LogInformation(
                "[LtmCatalogResolver] {DataAreaId}: tipos de contribuyente con {DocType}=[{Types}] | " +
                "grupo de cliente={Group}",
                dataAreaId, LtmCustMapping.CountryDocTypeRuc, string.Join(", ", taxPayerTypes),
                accountTypeGroup ?? "(ninguno)");

            return new LtmCompanyLocalization(taxPayerTypes, accountTypeGroup);
        }

        /// <summary>
        /// Tipos de contribuyente dados de alta para documento RUC en la legal entity. Con el
        /// alcance en RUC el par (documento, contribuyente) que identifica la fila se reduce a
        /// esta lista: <c>PN</c> para contacts y <c>PJ</c> para accounts.
        /// </summary>
        private async Task<IReadOnlyCollection<string>> LoadRucTaxPayerTypesAsync(
            string dataAreaId,
            CancellationToken cancellationToken)
        {
            var query = new QueryExpression(LtmCustMapping.VirtualDocTypeEntity)
            {
                ColumnSet = new ColumnSet(LtmCustMapping.VirtualTaxPayerTypeId),
                Criteria  =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            LtmCustMapping.VirtualDocTypeCompany, ConditionOperator.Equal, dataAreaId),
                        new ConditionExpression(
                            LtmCustMapping.VirtualDocTypeId,
                            ConditionOperator.Equal,
                            LtmCustMapping.CountryDocTypeRuc)
                    }
                }
            };

            var result = await Task.Run(() => _orgService.RetrieveMultiple(query), cancellationToken);

            return result.Entities
                .Select(e => e.GetAttributeValue<string>(LtmCustMapping.VirtualTaxPayerTypeId))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Grupo de cliente de la localizacion. Se busca la fila de la legal entity cuyo
        /// <c>CustVendEntity</c> es Customer — el unico dato del mapeo que no se navega sino
        /// que se consulta.
        ///
        /// <b>Si hay mas de una, no se adivina.</b> En INTE <c>caut</c> tiene dos ("Cliente
        /// Local" y "Cliente Exterior") y ningun criterio del repo puede elegir: ordenarlas
        /// alfabeticamente eligiria "Cliente Exterior" para clientes locales. Se omite el
        /// campo con las candidatas en el log, F&amp;O aplica su default, y la decision queda
        /// donde corresponde.
        /// </summary>
        private async Task<string?> LoadAccountTypeGroupAsync(
            string dataAreaId,
            CancellationToken cancellationToken)
        {
            var query = new QueryExpression(LtmCustMapping.VirtualAccountTypeGroupEntity)
            {
                ColumnSet = new ColumnSet(LtmCustMapping.VirtualAccountTypeGroupId),
                Criteria  =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            LtmCustMapping.VirtualAccountTypeGroupCompany,
                            ConditionOperator.Equal,
                            dataAreaId),
                        // El valor va como int: mserp_custvendentity es un Picklist, y
                        // filtrarlo con la etiqueta tira FormatException (ver LtmCustMapping).
                        new ConditionExpression(
                            LtmCustMapping.VirtualAccountTypeGroupCustVend,
                            ConditionOperator.Equal,
                            LtmCustMapping.CustVendEntityCustomer)
                    }
                }
            };

            var result = await Task.Run(() => _orgService.RetrieveMultiple(query), cancellationToken);

            var groups = result.Entities
                .Select(e => e.GetAttributeValue<string>(LtmCustMapping.VirtualAccountTypeGroupId))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!)
                .ToList();

            if (groups.Count == 1)
                return groups[0];

            // No se tira: el campo se omite y F&O aplica su default. Que falte el grupo, o que
            // haya varios, es configuracion del ERP y no un dato roto del cliente.
            if (groups.Count == 0)
                _logger.LogWarning(
                    "[LtmCatalogResolver] No hay grupo de cliente para {DataAreaId} en {Entity}. " +
                    "Se omite {Target} en el payload.",
                    dataAreaId, LtmCustMapping.VirtualAccountTypeGroupEntity,
                    LtmCustMapping.AccountTypeGroupId);
            else
                _logger.LogWarning(
                    "[LtmCatalogResolver] {DataAreaId} tiene {Count} grupos de cliente en {Entity} " +
                    "([{Groups}]): no hay criterio para elegir, se omite {Target} en el payload.",
                    dataAreaId, groups.Count, LtmCustMapping.VirtualAccountTypeGroupEntity,
                    string.Join(", ", groups), LtmCustMapping.AccountTypeGroupId);

            return null;
        }

        /// <summary>
        /// Estados/departamentos del pais, indexados por su propio codigo para poder buscar
        /// sin distinguir mayusculas. Sale de F&amp;O y no de Dataverse: es el catalogo contra
        /// el que el ERP va a validar lo que le mandemos.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, string>> LoadStatesAsync(
            string countryRegionId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[LtmCatalogResolver] Leyendo {EntitySet} de {Country} desde F&O.",
                LtmCustMapping.FoStateEntitySet, countryRegionId);

            var query = new FoODataQuery(LtmCustMapping.FoStateEntitySet)
            {
                Filter = $"{LtmCustMapping.FoStateCountryRegionField} eq '{countryRegionId}'",
                Select = LtmCustMapping.FoStateField
            };

            var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await foreach (var row in _foClient.QueryAsync<FoAddressState>(query, cancellationToken))
            {
                // Cross-company devuelve la misma tabla una vez por legal entity: los
                // duplicados son esperables.
                if (!string.IsNullOrWhiteSpace(row.State))
                    states[row.State] = row.State;
            }

            _logger.LogInformation(
                "[LtmCatalogResolver] {Country}: {Count} estados en {EntitySet}.",
                countryRegionId, states.Count, LtmCustMapping.FoStateEntitySet);

            return states;
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
