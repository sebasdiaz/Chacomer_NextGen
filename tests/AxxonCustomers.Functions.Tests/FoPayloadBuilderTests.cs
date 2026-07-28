using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El armado del payload que se POSTea a F&amp;O: resolucion de lookups, casing,
    /// omision de nulls y las guardas.
    /// </summary>
    public class FoPayloadBuilderTests
    {
        private static readonly Guid RecordId  = Guid.NewGuid();
        private static readonly Guid CompanyId = Guid.NewGuid();

        private readonly FakeOrganizationService _crm = new();

        /// <summary>Nombres de propiedad con el casing real de la API OData.</summary>
        private static FakeFoSchemaProvider Schema() => new(
            "dataAreaId", "PartyNumber", "IdentificationNumber", "CreditLimit",
            "CustomerAccount", "OnHoldStatus", "CreditRating", "PartyType");

        private FoPayloadBuilder Builder(IFoSchemaProvider? schema = null) =>
            new(_crm, schema ?? Schema(), NullLogger<FoPayloadBuilder>.Instance);

        private EntityReference GivenCompany(string? code = "cha") =>
            _crm.Add("cdm_company", CompanyId, ("cdm_companycode", code));

        private static EntityMap MapWith(params DualWriteFieldMapping[] rows) =>
            Given.Compile(Given.ExportWith(rows), Given.Overlay());

        // ── Payload ───────────────────────────────────────────────────

        [Fact]
        public async Task Resuelve_el_dataareaid_desde_la_compania()
        {
            var record = new Entity("account", RecordId) { ["msdyn_company"] = GivenCompany("cne") };

            var payload = await Builder().BuildAsync(record, MapWith());

            Assert.Equal("cne", payload.DataAreaId);
            Assert.Equal("cne", payload.Fields["dataAreaId"]);
        }

        [Fact]
        public async Task Resuelve_el_casing_del_campo_contra_fo()
        {
            // El export dice PARTYNUMBER; la API espera PartyNumber.
            var party  = _crm.Add("msdyn_party", Guid.NewGuid(), ("msdyn_partynumber", "PARTY-001"));
            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"] = GivenCompany(),
                ["msdyn_partyid"] = party
            };

            var payload = await Builder().BuildAsync(
                record, MapWith(Given.Row("msdyn_partyid.msdyn_partynumber", "PARTYNUMBER")));

            Assert.Equal("PARTY-001", payload.Fields["PartyNumber"]);
            Assert.DoesNotContain("PARTYNUMBER", payload.Fields.Keys);
        }

        [Fact]
        public async Task Un_campo_que_no_existe_en_fo_corta_el_mensaje()
        {
            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"] = GivenCompany(),
                ["msdyn_algo"]    = "valor"
            };

            var ex = await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => Builder().BuildAsync(record, MapWith(Given.Row("msdyn_algo", "CAMPO_INEXISTENTE"))));

            Assert.Contains("CAMPO_INEXISTENTE", ex.Message);
        }

        [Fact]
        public async Task Omite_los_nulls_para_que_fo_aplique_sus_defaults()
        {
            var record = new Entity("account", RecordId) { ["msdyn_company"] = GivenCompany() };

            var payload = await Builder().BuildAsync(
                record, MapWith(Given.Row("msdyn_identificationnumber", "IDENTIFICATIONNUMBER")));

            Assert.DoesNotContain("IdentificationNumber", payload.Fields.Keys);
        }

        [Fact]
        public async Task No_manda_el_write_back_en_el_post()
        {
            // F&O genera el CustomerAccount por number sequence.
            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"] = GivenCompany(),
                ["accountnumber"] = "C-000123"
            };

            var payload = await Builder().BuildAsync(record, MapWith());

            Assert.DoesNotContain("CustomerAccount", payload.Fields.Keys);
            // Pero si se conserva para el chequeo de idempotencia.
            Assert.Equal("C-000123", payload.MatchValues["CUSTOMERACCOUNT"]);
        }

        [Fact]
        public async Task Un_money_viaja_como_decimal()
        {
            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"] = GivenCompany(),
                ["creditlimit"]   = new Money(15000.50m)
            };

            var payload = await Builder().BuildAsync(
                record, MapWith(Given.Row("creditlimit", "CREDITLIMIT")));

            Assert.Equal(15000.50m, payload.Fields["CreditLimit"]);
        }

        [Fact]
        public async Task Un_optionset_mapeado_como_label_usa_la_etiqueta()
        {
            var overlay = Given.Overlay();
            overlay.Fields["msdyn_creditrating"] = new OverlayField { Kind = "label" };

            var map = Given.Compile(
                Given.ExportWith(Given.Row("msdyn_creditrating", "CREDITRATING")), overlay);

            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"]      = GivenCompany(),
                ["msdyn_creditrating"] = new OptionSetValue(1)
            };
            record.FormattedValues.Add("msdyn_creditrating", "Bueno");

            var payload = await Builder().BuildAsync(record, map);

            Assert.Equal("Bueno", payload.Fields["CreditRating"]);
        }

        // ── Value maps ────────────────────────────────────────────────

        [Fact]
        public async Task Traduce_el_optionset_al_literal_del_enum_de_fo()
        {
            var map = MapWith(Given.Row(
                "msdyn_onholdstatus", "ONHOLDSTATUS",
                new Dictionary<string, string> { ["No"] = "806380000", ["All"] = "806380002" }));

            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"]     = GivenCompany(),
                ["msdyn_onholdstatus"] = new OptionSetValue(806380002)
            };

            var payload = await Builder().BuildAsync(record, map);

            Assert.Equal("All", payload.Fields["OnHoldStatus"]);
        }

        [Fact]
        public async Task Un_valor_sin_equivalencia_se_omite_en_vez_de_viajar_crudo()
        {
            var map = MapWith(Given.Row(
                "msdyn_onholdstatus", "ONHOLDSTATUS",
                new Dictionary<string, string> { ["No"] = "806380000" }));

            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"]      = GivenCompany(),
                ["msdyn_onholdstatus"] = new OptionSetValue(999)   // sin equivalencia
            };

            var payload = await Builder().BuildAsync(record, map);

            Assert.DoesNotContain("OnHoldStatus", payload.Fields.Keys);
        }

        // ── Errores de mapeo y de datos ───────────────────────────────

        [Fact]
        public async Task Sin_compania_no_hay_dataareaid_y_el_mensaje_no_se_reintenta()
        {
            var record = new Entity("account", RecordId);

            var ex = await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => Builder().BuildAsync(record, MapWith()));

            Assert.Contains("msdyn_company", ex.Message);
        }

        [Fact]
        public async Task Una_compania_sin_codigo_no_se_reintenta()
        {
            var record = new Entity("account", RecordId) { ["msdyn_company"] = GivenCompany(code: null) };

            await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => Builder().BuildAsync(record, MapWith()));
        }

        [Fact]
        public async Task Un_lookup_mapeado_como_direct_avisa_que_es_un_error_de_mapeo()
        {
            var record = new Entity("account", RecordId)
            {
                ["msdyn_company"] = GivenCompany(),
                // Sin el path 'atributo.atributoRelacionado' esto es un EntityReference suelto.
                ["msdyn_partyid"] = new EntityReference("msdyn_party", Guid.NewGuid())
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Builder().BuildAsync(record, MapWith(Given.Row("msdyn_partyid", "PARTYNUMBER"))));

            Assert.Contains("lookup", ex.Message);
        }

        // ── Guarda de sincronizacion ──────────────────────────────────

        [Fact]
        public void Sin_condiciones_sincroniza_siempre()
        {
            var record = new Entity("account", RecordId);

            Assert.True(Builder().ShouldSync(record, MapWith(), out _));
        }

        [Fact]
        public void No_sincroniza_cuando_el_registro_no_cumple_la_condicion()
        {
            var overlay = Given.Overlay();
            overlay.SyncWhen.Add(new OverlayCondition
            {
                Attribute     = "customertypecode",
                ExpectedValue = Given.Json("3")
            });

            var map    = Given.Compile(Given.ExportWith(), overlay);
            var record = new Entity("account", RecordId) { ["customertypecode"] = new OptionSetValue(1) };

            Assert.False(Builder().ShouldSync(record, map, out var reason));
            Assert.Contains("customertypecode", reason);
        }

        [Fact]
        public void Sincroniza_cuando_cumple_la_condicion()
        {
            var overlay = Given.Overlay();
            overlay.SyncWhen.Add(new OverlayCondition
            {
                Attribute     = "customertypecode",
                ExpectedValue = Given.Json("3")
            });

            var map    = Given.Compile(Given.ExportWith(), overlay);
            var record = new Entity("account", RecordId) { ["customertypecode"] = new OptionSetValue(3) };

            Assert.True(Builder().ShouldSync(record, map, out _));
        }
    }
}
