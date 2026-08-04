using Axxon.Eip.Core.Dataverse;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El reparto entre Dual Write y la sincronizacion por API sale de un solo campo,
    /// <c>cdm_isenabledfordualwrite</c>, y la polaridad es peligrosa: el registro que
    /// mandamos al ERP es el de la company con el flag en <b>false</b>. Un campo sin
    /// setear no viene en el Retrieve y <c>GetAttributeValue&lt;bool&gt;</c> lo leeria
    /// como false — es decir, "mandalo". Estos tests fijan que eso no pase.
    /// </summary>
    public class DualWriteCompanyResolverTests
    {
        private static readonly Guid CompanyId = Guid.NewGuid();
        private static readonly Guid RecordId  = Guid.NewGuid();

        private readonly FakeOrganizationService _crm = new();
        private readonly DualWriteCompanyCache _cache = new();

        private DualWriteCompanyResolver Resolver() =>
            new(_crm, _cache, NullLogger<DualWriteCompanyResolver>.Instance);

        private void GivenCompany(string code = "cha", bool? dualWrite = null) =>
            _crm.Add(
                "cdm_company", CompanyId,
                ("cdm_companycode", code),
                ("cdm_isenabledfordualwrite", dualWrite));

        // ── El flag ───────────────────────────────────────────────────

        [Fact]
        public async Task La_company_en_dual_write_no_se_sincroniza_por_api()
        {
            GivenCompany(dualWrite: true);

            var company = await Resolver().ResolveAsync(CompanyId);

            Assert.Equal(CompanySyncHandling.DualWrite, company.Handling);
            Assert.Equal("cha", company.DataAreaId);
        }

        [Fact]
        public async Task La_company_fuera_de_dual_write_se_sincroniza_por_api()
        {
            GivenCompany("cne", dualWrite: false);

            var company = await Resolver().ResolveAsync(CompanyId);

            Assert.Equal(CompanySyncHandling.Api, company.Handling);
            Assert.Equal("cne", company.DataAreaId);
        }

        [Fact]
        public async Task El_flag_sin_setear_no_se_sincroniza()
        {
            // Sin el campo en el Retrieve. Si esto devolviera Api, un environment con el
            // campo despoblado mandaria el maestro de clientes entero a F&O.
            GivenCompany(dualWrite: null);

            var company = await Resolver().ResolveAsync(CompanyId);

            Assert.Equal(CompanySyncHandling.Unknown, company.Handling);
        }

        [Fact]
        public async Task La_company_inexistente_no_se_sincroniza()
        {
            var company = await Resolver().ResolveAsync(Guid.NewGuid());

            Assert.Equal(CompanySyncHandling.Unknown, company.Handling);
        }

        [Fact]
        public async Task La_company_vacia_no_se_sincroniza()
        {
            var company = await Resolver().ResolveAsync(Guid.Empty);

            Assert.Equal(CompanySyncHandling.Unknown, company.Handling);
            Assert.Empty(_crm.Retrieves);
        }

        // ── Desde el registro ─────────────────────────────────────────

        [Fact]
        public async Task Resuelve_la_company_del_registro()
        {
            GivenCompany("cne", dualWrite: false);
            _crm.Add("account", RecordId, ("msdyn_company", new EntityReference("cdm_company", CompanyId)));

            var company = await Resolver().ResolveForRecordAsync("account", RecordId);

            Assert.Equal(CompanySyncHandling.Api, company.Handling);
            Assert.Equal("cne", company.DataAreaId);
        }

        [Fact]
        public async Task El_registro_sin_company_no_se_sincroniza()
        {
            _crm.Add("account", RecordId, ("name", "Sin legal entity"));

            var company = await Resolver().ResolveForRecordAsync("account", RecordId);

            Assert.Equal(CompanySyncHandling.Unknown, company.Handling);
        }

        [Fact]
        public async Task El_registro_inexistente_no_se_sincroniza()
        {
            var company = await Resolver().ResolveForRecordAsync("account", Guid.NewGuid());

            Assert.Equal(CompanySyncHandling.Unknown, company.Handling);
        }

        // ── Cache ─────────────────────────────────────────────────────

        [Fact]
        public async Task La_company_se_lee_una_sola_vez_dentro_de_la_ventana()
        {
            GivenCompany(dualWrite: true);
            var resolver = Resolver();

            await resolver.ResolveAsync(CompanyId);
            await resolver.ResolveAsync(CompanyId);

            Assert.Single(_crm.Retrieves, r => r.LogicalName == "cdm_company");
        }

        [Fact]
        public async Task Vencida_la_ventana_la_company_se_vuelve_a_leer()
        {
            // El flag lo cambia un admin cuando una legal entity entra a Dual Write: ese
            // cambio no puede quedar pegado hasta que recicle la instancia.
            GivenCompany(dualWrite: true);
            _cache.Ttl = TimeSpan.Zero;
            var resolver = Resolver();

            await resolver.ResolveAsync(CompanyId);
            await resolver.ResolveAsync(CompanyId);

            Assert.Equal(2, _crm.Retrieves.Count(r => r.LogicalName == "cdm_company"));
        }
    }
}
