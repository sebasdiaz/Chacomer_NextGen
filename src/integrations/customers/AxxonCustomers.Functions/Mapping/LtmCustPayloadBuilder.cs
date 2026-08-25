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
    /// y este mapeo no entra en ellas: hace una <b>consulta con filtro</b> para el grupo de
    /// cliente, sale a una relacion <b>1:N</b> (<c>customeraddress</c>) de la que hay que
    /// elegir una fila, y tiene <b>un atributo que alimenta dos campos</b> de F&amp;O
    /// (<c>axx_tipodocumento</c> da <c>CountryDocTypeId</c> y <c>TaxPayerTypeId</c>, y el
    /// motor indexa los mapeos por atributo de CRM). Agregar esas primitivas al JSON lo
    /// convertiria en un mini-lenguaje de queries al servicio de un solo consumidor.
    /// Ver ADR-001.
    ///
    /// Las cadenas son las mismas para contact y para account; lo unico que cambia es de
    /// donde sale el <c>AccountNum</c> (ver <see cref="LtmCustSource"/>).
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

        public async Task<LtmCustPayload> BuildAsync(
            Entity record,
            LtmCustSource source,
            string accountNum,
            CancellationToken cancellationToken = default)
        {
            var dataAreaId = ResolveDataAreaId(record, source);

            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [LtmCustMapping.DataAreaId] = dataAreaId,
                [LtmCustMapping.AccountNum] = accountNum
            };

            // El RUC alimenta los dos campos de documento: el del pais y el del estado.
            var identificationNumber = record.GetAttributeValue<string>(
                LtmCustMapping.IdentificationNumberAttribute);

            values[LtmCustMapping.CountryDocNum] = identificationNumber;
            values[LtmCustMapping.StateDocNum]   = identificationNumber;

            // Los dos codigos salen de la misma fila de la virtual entity de tipos de documento.
            var docType = await ResolveDocTypeAsync(record, source, cancellationToken);
            values[LtmCustMapping.CountryDocTypeId] = docType?.DocTypeId;
            values[LtmCustMapping.TaxPayerTypeId]   = docType?.TaxPayerTypeId;

            // Depende solo de la legal entity, no del cliente.
            values[LtmCustMapping.AccountTypeGroupId] =
                await _catalogs.ResolveAccountTypeGroupAsync(dataAreaId, cancellationToken);

            // Pais y region salen de la direccion primaria, no del registro principal.
            var address = ResolvePrimaryAddress(record, source);
            values[LtmCustMapping.CountryRegionId] = ResolveThroughLookup(
                address, LtmCustMapping.AddressCountryLookup, LtmCustMapping.CountryCodeAttribute);
            values[LtmCustMapping.StateId] = ResolveThroughLookup(
                address, LtmCustMapping.AddressStateLookup, LtmCustMapping.StateNameAttribute);

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

        // ── Tipo de documento ─────────────────────────────────────────

        private async Task<LtmDocType?> ResolveDocTypeAsync(
            Entity record,
            LtmCustSource source,
            CancellationToken cancellationToken)
        {
            var docTypeRef = record.GetAttributeValue<EntityReference>(LtmCustMapping.DocTypeAttribute);

            if (docTypeRef is null)
            {
                _logger.LogWarning(
                    "[LtmCustPayloadBuilder] El {Entity} {RecordId} no tiene {Attribute}: " +
                    "se omiten {DocType} y {TaxPayer}.",
                    source.EntityLogicalName, record.Id, LtmCustMapping.DocTypeAttribute,
                    LtmCustMapping.CountryDocTypeId, LtmCustMapping.TaxPayerTypeId);
                return null;
            }

            // El lookup apunta directo a la virtual entity (cacheada; va contra F&O).
            return await _catalogs.ResolveDocTypeAsync(docTypeRef.Id, cancellationToken);
        }

        // ── Direccion primaria (relacion 1:N) ─────────────────────────

        /// <summary>
        /// De las direcciones del cliente se usa la primaria (<c>addressnumber = 1</c>), que es
        /// la que el formulario muestra como Address 1. Un cliente sin direccion no es un error:
        /// se omiten pais y region y el resto del payload viaja igual.
        /// </summary>
        private Entity? ResolvePrimaryAddress(Entity record, LtmCustSource source)
        {
            var query = new QueryExpression(LtmCustMapping.AddressEntity)
            {
                ColumnSet = new ColumnSet(
                    LtmCustMapping.AddressCountryLookup,
                    LtmCustMapping.AddressStateLookup),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            LtmCustMapping.AddressParentAttribute, ConditionOperator.Equal, record.Id),
                        new ConditionExpression(
                            LtmCustMapping.AddressNumberAttribute,
                            ConditionOperator.Equal,
                            LtmCustMapping.PrimaryAddressNumber)
                    }
                }
            };

            var result = _orgService.RetrieveMultiple(query);

            if (result.Entities.Count == 0)
            {
                _logger.LogWarning(
                    "[LtmCustPayloadBuilder] El {Entity} {RecordId} no tiene direccion primaria " +
                    "({AddressEntity} con {NumberAttribute}={Number}): se omiten {Country} y {State}.",
                    source.EntityLogicalName, record.Id, LtmCustMapping.AddressEntity,
                    LtmCustMapping.AddressNumberAttribute, LtmCustMapping.PrimaryAddressNumber,
                    LtmCustMapping.CountryRegionId, LtmCustMapping.StateId);
                return null;
            }

            return result.Entities[0];
        }

        /// <summary>Navega un lookup de la direccion y devuelve el campo pedido de la fila destino.</summary>
        private string? ResolveThroughLookup(Entity? address, string lookupAttribute, string targetAttribute)
        {
            var reference = address?.GetAttributeValue<EntityReference>(lookupAttribute);

            if (reference is null)
                return null;

            var related = _orgService.Retrieve(
                reference.LogicalName,
                reference.Id,
                new ColumnSet(targetAttribute));

            return related.GetAttributeValue<string>(targetAttribute);
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
