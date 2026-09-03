using AxxonCustomers.Functions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace AxxonCustomers.Functions.Mapping
{
    /// <summary>Payload listo para el upsert contra <c>LTMCustTable</c>.</summary>
    public sealed class LtmCustPayload
    {
        /// <summary>Campos del POST, con el nombre de propiedad OData ya resuelto.</summary>
        public required IDictionary<string, object?> Fields { get; init; }

        public required string DataAreaId { get; init; }

        public required string AccountNum { get; init; }
    }

    /// <summary>
    /// Arma el payload de <c>LTMCustTable</c> a partir de un contact o un account.
    ///
    /// <b>Por que esto es C# y no un overlay JSON como CustomersV3.</b> El motor de mapeo
    /// declarativo tiene cinco primitivas cerradas a proposito (ver <see cref="FieldKind"/>),
    /// y este mapeo no entra en ellas: hace <b>consultas con filtro</b> sobre los catalogos de
    /// la localizacion, sale a una relacion <b>1:N</b> (<c>customeraddress</c>) de la que hay
    /// que elegir una fila, valida un valor contra un catalogo de F&amp;O, y tiene <b>un
    /// atributo que alimenta dos campos</b> del ERP (el RUC da <c>CountryDocNum</c> y
    /// <c>StateDocNum</c>, y el motor indexa los mapeos por atributo de CRM). Ver ADR-001.
    ///
    /// <b>El alcance de la v1 es RUC y Paraguay</b>, asi que el tipo de documento y el pais
    /// son constantes y el tipo de contribuyente sale del tipo de registro. Eso deja al mapeo
    /// leyendo del cliente una sola cosa —el RUC— mas la company y la direccion.
    /// </summary>
    public sealed class LtmCustPayloadBuilder
    {
        private readonly IOrganizationService _orgService;
        private readonly LtmCatalogResolver _catalogs;
        private readonly IFoSchemaProvider _schema;
        private readonly ILogger<LtmCustPayloadBuilder> _logger;

        public LtmCustPayloadBuilder(
            IOrganizationService orgService,
            LtmCatalogResolver catalogs,
            IFoSchemaProvider schema,
            ILogger<LtmCustPayloadBuilder> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _catalogs   = catalogs   ?? throw new ArgumentNullException(nameof(catalogs));
            _schema     = schema     ?? throw new ArgumentNullException(nameof(schema));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Columnas del registro principal que hay que traer de Dataverse.</summary>
        public static ColumnSet ColumnsFor(LtmCustSource source) => new(source.Columns);

        /// <summary>
        /// Arma el payload, o devuelve <c>null</c> si la legal entity del cliente no tiene la
        /// localizacion PY configurada — o sea, si el cliente esta fuera del alcance funcional.
        /// No es un error: hay legal entities en el environment (las de USA y Alemania, entre
        /// otras) que no llevan <c>LTMCustTable</c> y que a la cola llegan igual.
        /// </summary>
        public async Task<LtmCustPayload?> BuildAsync(
            Entity record,
            LtmCustSource source,
            string accountNum,
            CancellationToken cancellationToken = default)
        {
            var dataAreaId = ResolveDataAreaId(record, source);

            // Guarda de alcance: la localizacion PY solo aplica donde el ERP la tiene
            // configurada. Se deriva del ERP y no de una lista de companies en el repo, que
            // habria que acordarse de tocar cada vez que el ERP cambie.
            var localization = await _catalogs.ResolveCompanyAsync(dataAreaId, cancellationToken);

            if (!localization.IsConfigured)
            {
                _logger.LogInformation(
                    "[LtmCustPayloadBuilder] La legal entity {DataAreaId} del {Entity} {RecordId} no " +
                    "tiene la localizacion PY configurada: el cliente esta fuera del alcance y no se " +
                    "sincroniza LTMCustTable.",
                    dataAreaId, source.EntityLogicalName, record.Id);

                return null;
            }

            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [LtmCustMapping.DataAreaId] = dataAreaId,
                [LtmCustMapping.AccountNum] = accountNum,

                // Constantes del alcance: RUC y Paraguay.
                [LtmCustMapping.CountryDocTypeId] = LtmCustMapping.CountryDocTypeRuc,
                [LtmCustMapping.CountryRegionId]  = LtmCustMapping.CountryRegionParaguay
            };

            // El RUC alimenta los dos campos de documento: el del pais y el del estado.
            var identificationNumber = record.GetAttributeValue<string>(
                LtmCustMapping.IdentificationNumberAttribute);

            values[LtmCustMapping.CountryDocNum] = identificationNumber;
            values[LtmCustMapping.StateDocNum]   = identificationNumber;

            values[LtmCustMapping.TaxPayerTypeId]     = ResolveTaxPayerType(record, source, localization);
            values[LtmCustMapping.AccountTypeGroupId] = localization.AccountTypeGroupId;
            values[LtmCustMapping.StateId]            = await ResolveStateAsync(record, source, cancellationToken);

            return await MaterializeAsync(values, dataAreaId, accountNum, cancellationToken);
        }

        // ── dataAreaId ────────────────────────────────────────────────

        /// <summary>
        /// La legal entity sale de <c>msdyn_company</c> del propio registro, igual que en el
        /// overlay de CustomersV3.
        ///
        /// El mapeo funcional la resolvia por <c>systemuser.cdm_company</c> — el usuario que
        /// ejecuta —, que es correcto pensado desde CRM (quien carga el cliente pertenece a su
        /// company, que es como Dual Write resuelve la particion) pero no aca: en una Function
        /// el usuario que ejecuta es el application user de la Managed Identity, con una unica
        /// company fija. Todos los clientes caerian en la misma legal entity, y encima en una
        /// distinta de la que uso CustomersV3, con lo cual el AccountNum no existiria ahi.
        /// </summary>
        private string ResolveDataAreaId(Entity record, LtmCustSource source)
        {
            var companyRef = record.GetAttributeValue<EntityReference>(LtmCustMapping.CompanyAttribute);

            if (companyRef is null)
                throw new NonRetryableSyncException(
                    $"El registro {source.EntityLogicalName} {record.Id} no tiene compania " +
                    $"({LtmCustMapping.CompanyAttribute}): no se puede determinar el dataAreaId " +
                    "para LTMCustTable.");

            var company = _orgService.Retrieve(
                companyRef.LogicalName,
                companyRef.Id,
                new ColumnSet(LtmCustMapping.CompanyCodeAttribute));

            var dataAreaId = company.GetAttributeValue<string>(LtmCustMapping.CompanyCodeAttribute);

            if (string.IsNullOrWhiteSpace(dataAreaId))
                throw new NonRetryableSyncException(
                    $"La compania {companyRef.Id} del registro {record.Id} no tiene " +
                    $"{LtmCustMapping.CompanyCodeAttribute}.");

            return dataAreaId;
        }

        // ── Tipo de contribuyente ─────────────────────────────────────

        /// <summary>
        /// Con el documento fijo en RUC, el tipo de contribuyente es lo unico que queda para
        /// identificar la fila del catalogo, y sale del tipo de registro: <c>PN</c> para
        /// contacts, <c>PJ</c> para accounts (ver <see cref="LtmCustSource"/>).
        ///
        /// Se confirma contra el catalogo de la company antes de mandarlo: la combinacion
        /// existe en las legal entities que miramos, pero es configuracion del ERP y puede no
        /// estar. Si falta, se omite el campo con un warning en vez de mandar un codigo que
        /// F&amp;O va a rechazar con un 400.
        /// </summary>
        private string? ResolveTaxPayerType(
            Entity record,
            LtmCustSource source,
            LtmCompanyLocalization localization)
        {
            if (localization.RucTaxPayerTypes.Contains(source.TaxPayerTypeId, StringComparer.OrdinalIgnoreCase))
                return source.TaxPayerTypeId;

            _logger.LogWarning(
                "[LtmCustPayloadBuilder] El {Entity} {RecordId} deberia ser {TaxPayerType}, pero esa " +
                "combinacion con {DocType} no existe en {Entity2} para su legal entity " +
                "(hay [{Disponibles}]). Se omite {Target}.",
                source.EntityLogicalName, record.Id, source.TaxPayerTypeId,
                LtmCustMapping.CountryDocTypeRuc, LtmCustMapping.VirtualDocTypeEntity,
                string.Join(", ", localization.RucTaxPayerTypes), LtmCustMapping.TaxPayerTypeId);

            return null;
        }

        // ── Estado, desde la direccion (relacion 1:N) ─────────────────

        /// <summary>
        /// El estado sale de la direccion del cliente, y se valida contra el catalogo de
        /// F&amp;O antes de viajar (ver <see cref="LtmCatalogResolver.ResolveStateAsync"/>).
        ///
        /// <b>Cual direccion.</b> La primera —por numero— que tenga el campo cargado, no la
        /// <c>addressnumber = 1</c>: Dataverse crea automaticamente las direcciones 1 y 2 de
        /// cada cliente y casi nunca se completan, asi que filtrar por la 1 daba vacio en la
        /// mayoria de los clientes. Un cliente sin direccion con estado no es un error: se
        /// omite el campo y el resto del payload viaja igual.
        /// </summary>
        private async Task<string?> ResolveStateAsync(
            Entity record,
            LtmCustSource source,
            CancellationToken cancellationToken)
        {
            var query = new QueryExpression(LtmCustMapping.AddressEntity)
            {
                ColumnSet = new ColumnSet(
                    LtmCustMapping.AddressStateAttribute,
                    LtmCustMapping.AddressNumberAttribute),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            LtmCustMapping.AddressParentAttribute, ConditionOperator.Equal, record.Id),
                        new ConditionExpression(
                            LtmCustMapping.AddressStateAttribute, ConditionOperator.NotNull)
                    }
                },
                Orders = { new OrderExpression(LtmCustMapping.AddressNumberAttribute, OrderType.Ascending) }
            };

            var result = _orgService.RetrieveMultiple(query);

            if (result.Entities.Count == 0)
            {
                _logger.LogInformation(
                    "[LtmCustPayloadBuilder] El {Entity} {RecordId} no tiene ninguna direccion con " +
                    "{Attribute}: se omite {Target}.",
                    source.EntityLogicalName, record.Id, LtmCustMapping.AddressStateAttribute,
                    LtmCustMapping.StateId);

                return null;
            }

            var candidate = result.Entities[0].GetAttributeValue<string>(LtmCustMapping.AddressStateAttribute);

            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            var state = await _catalogs.ResolveStateAsync(
                LtmCustMapping.CountryRegionParaguay, candidate, cancellationToken);

            if (state is null)
                _logger.LogWarning(
                    "[LtmCustPayloadBuilder] El {Entity} {RecordId} tiene {Attribute}='{Candidate}', " +
                    "que no existe en el catalogo de estados de {Country}. Se omite {Target} en vez de " +
                    "mandarlo y que F&O rechace la fila entera.",
                    source.EntityLogicalName, record.Id, LtmCustMapping.AddressStateAttribute, candidate,
                    LtmCustMapping.CountryRegionParaguay, LtmCustMapping.StateId);

            return state;
        }

        // ── Materializacion ───────────────────────────────────────────

        /// <summary>
        /// Resuelve el casing real de cada propiedad OData y arma el body del POST.
        ///
        /// Los nulls y los vacios se omiten, igual que en <see cref="FoPayloadBuilder"/>:
        /// el mapeo no sabe distinguir "el usuario borro el dato" de "el campo nunca se
        /// completo", y mandar null pisa datos que pueden venir de otra fuente del ERP.
        /// </summary>
        private async Task<LtmCustPayload> MaterializeAsync(
            IReadOnlyDictionary<string, object?> values,
            string dataAreaId,
            string accountNum,
            CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var (field, value) in values)
            {
                if (value is null || (value is string text && string.IsNullOrWhiteSpace(text)))
                    continue;

                var target = await _schema.ResolvePropertyAsync(
                    LtmCustMapping.EntitySet, field, cancellationToken);

                if (target is null)
                    throw new NonRetryableSyncException(
                        $"El mapeo de LTMCustTable apunta al campo '{field}', que no existe en " +
                        $"{LtmCustMapping.EntitySet}. Revisar LtmCustMapping contra la metadata de F&O.");

                fields[target] = value;
            }

            return new LtmCustPayload
            {
                Fields     = fields,
                DataAreaId = dataAreaId,
                AccountNum = accountNum
            };
        }
    }
}
