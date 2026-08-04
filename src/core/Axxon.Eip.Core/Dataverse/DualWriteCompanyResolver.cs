using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Axxon.Eip.Core.Dataverse
{
    /// <summary>Quien se hace cargo de llevar el registro a F&amp;O.</summary>
    public enum CompanySyncHandling
    {
        /// <summary>La legal entity la sincroniza Dual Write. Nosotros no tocamos nada.</summary>
        DualWrite,

        /// <summary>La legal entity NO esta en Dual Write: la sincronizamos por API.</summary>
        Api,

        /// <summary>
        /// No se pudo determinar (company inexistente o <c>cdm_isenabledfordualwrite</c>
        /// sin setear). No se sincroniza: ver <see cref="DualWriteCompanyResolver"/>.
        /// </summary>
        Unknown
    }

    /// <summary>Estado de una legal entity respecto de Dual Write.</summary>
    public sealed record DualWriteCompany(
        Guid CompanyId,
        string? DataAreaId,
        CompanySyncHandling Handling);

    public interface IDualWriteCompanyResolver
    {
        /// <summary>
        /// Resuelve como se sincroniza la legal entity. Nunca tira si la company no
        /// existe: devuelve <see cref="CompanySyncHandling.Unknown"/>.
        /// </summary>
        Task<DualWriteCompany> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Igual que <see cref="ResolveAsync"/> pero partiendo del registro: lee el lookup
        /// a la company y resuelve. Un registro sin company da
        /// <see cref="CompanySyncHandling.Unknown"/>.
        /// </summary>
        Task<DualWriteCompany> ResolveForRecordAsync(
            string entityLogicalName,
            Guid recordId,
            string companyAttribute = DualWriteCompanyResolver.DefaultCompanyAttribute,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Cache del resolver, singleton. Va separado del resolver porque el resolver
    /// depende de IOrganizationService, que en algunas apps es transient.
    /// </summary>
    public sealed class DualWriteCompanyCache
    {
        internal readonly ConcurrentDictionary<Guid, (DualWriteCompany Value, DateTimeOffset LoadedAt)> Entries = new();

        /// <summary>
        /// Ventana de cache. El flag lo cambia un admin cuando una legal entity entra a
        /// Dual Write, y ese cambio redirige el trafico de toda la company: no puede
        /// quedar pegado hasta que recicle la instancia.
        /// </summary>
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(15);
    }

    /// <summary>
    /// Determina si una legal entity ya la sincroniza Dual Write, leyendo
    /// <c>cdm_isenabledfordualwrite</c> de <c>cdm_company</c>.
    ///
    /// <b>La polaridad importa.</b> El registro que sincronizamos por API es el de la
    /// company con el flag en <c>false</c>. En Dataverse un Yes/No que nunca se seteo no
    /// viene en el Retrieve, y <c>GetAttributeValue&lt;bool&gt;</c> devolveria <c>false</c>
    /// — es decir, "sincronizala". Con el campo despoblado eso mandaria el maestro de
    /// clientes entero a F&amp;O. Por eso el flag ausente es
    /// <see cref="CompanySyncHandling.Unknown"/> y no se sincroniza: ante la duda, no
    /// escribimos en el ERP.
    /// </summary>
    public sealed class DualWriteCompanyResolver : IDualWriteCompanyResolver
    {
        public const string EntityLogicalName = "cdm_company";
        public const string CompanyCodeField  = "cdm_companycode";
        public const string DualWriteFlag     = "cdm_isenabledfordualwrite";

        /// <summary>Lookup a la legal entity en account y contact (el de Dual Write).</summary>
        public const string DefaultCompanyAttribute = "msdyn_company";

        private readonly IOrganizationService _orgService;
        private readonly DualWriteCompanyCache _cache;
        private readonly ILogger<DualWriteCompanyResolver> _logger;

        public DualWriteCompanyResolver(
            IOrganizationService orgService,
            DualWriteCompanyCache cache,
            ILogger<DualWriteCompanyResolver> logger)
        {
            _orgService = orgService ?? throw new ArgumentNullException(nameof(orgService));
            _cache      = cache      ?? throw new ArgumentNullException(nameof(cache));
            _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DualWriteCompany> ResolveAsync(
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            if (companyId == Guid.Empty)
                return new DualWriteCompany(companyId, null, CompanySyncHandling.Unknown);

            if (_cache.Entries.TryGetValue(companyId, out var cached) &&
                DateTimeOffset.UtcNow - cached.LoadedAt < _cache.Ttl)
                return cached.Value;

            var resolved = await Task.Run(() => Load(companyId), cancellationToken);

            _cache.Entries[companyId] = (resolved, DateTimeOffset.UtcNow);
            return resolved;
        }

        public async Task<DualWriteCompany> ResolveForRecordAsync(
            string entityLogicalName,
            Guid recordId,
            string companyAttribute = DefaultCompanyAttribute,
            CancellationToken cancellationToken = default)
        {
            EntityReference? companyRef;

            try
            {
                var record = await Task.Run(
                    () => _orgService.Retrieve(entityLogicalName, recordId, new ColumnSet(companyAttribute)),
                    cancellationToken);

                companyRef = record.GetAttributeValue<EntityReference>(companyAttribute);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[DualWriteCompanyResolver] No se pudo leer '{CompanyAttribute}' de {Entity} " +
                    "{RecordId}: {Error}. Se trata como indeterminado.",
                    companyAttribute, entityLogicalName, recordId, ex.Message);

                return new DualWriteCompany(Guid.Empty, null, CompanySyncHandling.Unknown);
            }

            if (companyRef is null)
            {
                _logger.LogWarning(
                    "[DualWriteCompanyResolver] El {Entity} {RecordId} no tiene '{CompanyAttribute}': " +
                    "no se puede determinar la legal entity.",
                    entityLogicalName, recordId, companyAttribute);

                return new DualWriteCompany(Guid.Empty, null, CompanySyncHandling.Unknown);
            }

            return await ResolveAsync(companyRef.Id, cancellationToken);
        }

        private DualWriteCompany Load(Guid companyId)
        {
            Entity company;

            try
            {
                company = _orgService.Retrieve(
                    EntityLogicalName, companyId, new ColumnSet(CompanyCodeField, DualWriteFlag));
            }
            catch (Exception ex)
            {
                // Una company que no existe es un dato roto, no un error transitorio:
                // se resuelve como Unknown y el registro no se sincroniza.
                _logger.LogWarning(ex,
                    "[DualWriteCompanyResolver] No se pudo recuperar la company {CompanyId}: {Error}. " +
                    "Se trata como indeterminada (no se sincroniza por API).",
                    companyId, ex.Message);

                return new DualWriteCompany(companyId, null, CompanySyncHandling.Unknown);
            }

            var dataAreaId = company.GetAttributeValue<string>(CompanyCodeField);

            // Contains explicito: GetAttributeValue<bool> sobre un campo ausente devuelve
            // false, que es justo el valor que dispara la sincronizacion por API.
            if (!company.Contains(DualWriteFlag) || company[DualWriteFlag] is not bool isEnabled)
            {
                _logger.LogWarning(
                    "[DualWriteCompanyResolver] La company {CompanyId} ({DataAreaId}) no tiene " +
                    "'{Flag}' seteado. No se sincroniza por API: hay que definir el flag en Dataverse.",
                    companyId, dataAreaId ?? "sin codigo", DualWriteFlag);

                return new DualWriteCompany(companyId, dataAreaId, CompanySyncHandling.Unknown);
            }

            var handling = isEnabled ? CompanySyncHandling.DualWrite : CompanySyncHandling.Api;

            _logger.LogInformation(
                "[DualWriteCompanyResolver] Company {CompanyId} ({DataAreaId}): {Flag}={IsEnabled} " +
                "=> {Handling}.",
                companyId, dataAreaId ?? "sin codigo", DualWriteFlag, isEnabled, handling);

            return new DualWriteCompany(companyId, dataAreaId, handling);
        }
    }
}
