using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El mapeo hacia LTMCustTable. A diferencia del de CustomersV3 no se declara en JSON
    /// (navega cadenas de dos saltos, consulta con filtro y sale a una relacion 1:N), asi
    /// que las decisiones que en aquel afirma <c>ShippedMappingsTests</c> sobre los archivos,
    /// aca se afirman sobre el codigo.
    /// </summary>
    public class LtmCustPayloadBuilderTests
    {
        private const string DataAreaId = "caut";
        private const string AccountNum = "CAUT-000012";
        private const string Ruc        = "80098873-6";

        [Fact]
        public async Task Mapea_las_cadenas_del_funcional()
        {
            var (builder, record) = GivenContact();

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.Equal(DataAreaId,  payload.Fields[LtmCustMapping.DataAreaId]);
            Assert.Equal(AccountNum,  payload.Fields[LtmCustMapping.AccountNum]);
            Assert.Equal("RUC",       payload.Fields[LtmCustMapping.CountryDocTypeId]);
            Assert.Equal("PJ",        payload.Fields[LtmCustMapping.TaxPayerTypeId]);
            Assert.Equal("Cliente Local", payload.Fields[LtmCustMapping.AccountTypeGroupId]);
            Assert.Equal("PRY",       payload.Fields[LtmCustMapping.CountryRegionId]);
            Assert.Equal("Asuncion",  payload.Fields[LtmCustMapping.StateId]);
        }

        [Fact]
        public async Task El_ruc_alimenta_los_dos_campos_de_documento()
        {
            var (builder, record) = GivenContact();

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            // CountryDocNum y StateDocNum salen los dos de msdyn_identificationnumber: es el
            // unico atributo de CRM que alimenta dos campos de F&O.
            Assert.Equal(Ruc, payload.Fields[LtmCustMapping.CountryDocNum]);
            Assert.Equal(Ruc, payload.Fields[LtmCustMapping.StateDocNum]);
        }

        [Fact]
        public async Task El_dataAreaId_sale_del_registro_y_no_del_usuario()
        {
            // El mapeo funcional lo resolvia por systemuser.cdm_company. En una Function el
            // usuario que ejecuta es el application user de la Managed Identity, con una unica
            // company fija: todos los clientes caerian en la misma legal entity, y en una
            // distinta de la que uso CustomersV3.
            var (builder, record) = GivenContact(companyCode: "otra");

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.Equal("otra", payload.Fields[LtmCustMapping.DataAreaId]);
        }

        [Fact]
        public async Task Sin_company_no_se_puede_resolver_la_legal_entity()
        {
            var (builder, record) = GivenContact(withCompany: false);

            await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => builder.BuildAsync(record, LtmCustSource.Contact, AccountNum));
        }

        [Fact]
        public async Task Sin_direccion_primaria_se_omiten_pais_y_region_pero_el_resto_viaja()
        {
            var (builder, record) = GivenContact(withAddress: false);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.CountryRegionId));
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.StateId));
            Assert.Equal(Ruc, payload.Fields[LtmCustMapping.CountryDocNum]);
        }

        [Fact]
        public async Task Sin_tipo_de_documento_se_omiten_sus_dos_campos()
        {
            var (builder, record) = GivenContact(withDocType: false);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.CountryDocTypeId));
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.TaxPayerTypeId));
        }

        [Fact]
        public async Task Solo_se_usa_la_direccion_primaria()
        {
            // La segunda direccion tiene otro pais: si el builder la tomara, se veria aca.
            var (builder, record) = GivenContact(withSecondAddress: true);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.Equal("PRY", payload.Fields[LtmCustMapping.CountryRegionId]);
        }

        [Fact]
        public async Task Los_campos_vacios_se_omiten()
        {
            // Vaciar un campo en CRM no lo vacia en F&O: el mapeo no sabe distinguir "el
            // usuario borro el dato" de "nunca se completo". Mismo criterio que CustomersV3.
            var (builder, record) = GivenContact(ruc: "   ");

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.CountryDocNum));
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.StateDocNum));
        }

        [Fact]
        public async Task Los_catalogos_de_virtual_entities_se_leen_una_sola_vez()
        {
            // Cada Retrieve sobre una virtual entity es Dataverse llamando en vivo a F&O:
            // sin cache, cada mensaje sumaria dos viajes al ERP.
            var (builder, record, service) = GivenContactWithService();

            await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);
            await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.Equal(1, service.Retrieves.Count(r => r.LogicalName == LtmCustMapping.VirtualDocTypeEntity));
            Assert.Equal(1, service.RetrieveMultiples.Count(e => e == LtmCustMapping.VirtualAccountTypeGroupEntity));
        }

        [Fact]
        public void Contact_y_account_solo_difieren_en_el_campo_del_AccountNum()
        {
            Assert.Equal("msdyn_contactpersonid", LtmCustSource.Contact.AccountNumberAttribute);
            Assert.Equal("accountnumber",         LtmCustSource.Account.AccountNumberAttribute);

            // Las cadenas de navegacion son las mismas para los dos, asi que las columnas del
            // Retrieve solo se diferencian en el write-back.
            Assert.Equal(
                LtmCustSource.Contact.Columns.Where(c => c != LtmCustSource.Contact.AccountNumberAttribute),
                LtmCustSource.Account.Columns.Where(c => c != LtmCustSource.Account.AccountNumberAttribute));
        }

        [Fact]
        public void Una_entidad_que_no_es_contact_ni_account_no_resuelve()
        {
            Assert.Null(LtmCustSource.For("lead"));
            Assert.Null(LtmCustSource.For(null));
            Assert.Same(LtmCustSource.Contact, LtmCustSource.For("Contact"));
        }

        [Fact]
        public async Task Un_campo_que_no_existe_en_FO_no_se_manda_a_ciegas()
        {
            // El casing de las propiedades OData no es derivable y se resuelve contra el
            // environment. Si un campo del mapeo no existe en el entity set, el mensaje va al
            // DLQ en vez de escribir mal en el ERP.
            var (_, record, service) = GivenContactWithService();

            var builder = new LtmCustPayloadBuilder(
                service,
                new LtmCatalogResolver(service, new LtmCatalogCache(), NullLogger<LtmCatalogResolver>.Instance),
                new FakeFoSchemaProvider(LtmCustMapping.DataAreaId),
                NullLogger<LtmCustPayloadBuilder>.Instance);

            await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => builder.BuildAsync(record, LtmCustSource.Contact, AccountNum));
        }

        // ── Armado ────────────────────────────────────────────────────

        /// <summary>Las propiedades de LTMCustTable, con el casing del registro real de F&amp;O.</summary>
        private static FakeFoSchemaProvider LtmSchema() => new(
            LtmCustMapping.DataAreaId,
            LtmCustMapping.AccountNum,
            LtmCustMapping.CountryDocTypeId,
            LtmCustMapping.CountryDocNum,
            LtmCustMapping.StateDocNum,
            LtmCustMapping.TaxPayerTypeId,
            LtmCustMapping.AccountTypeGroupId,
            LtmCustMapping.CountryRegionId,
            LtmCustMapping.StateId);


        private static (LtmCustPayloadBuilder Builder, Entity Record) GivenContact(
            string companyCode = DataAreaId,
            string? ruc = Ruc,
            bool withCompany = true,
            bool withAddress = true,
            bool withDocType = true,
            bool withSecondAddress = false)
        {
            var (builder, record, _) = GivenContactWithService(
                companyCode, ruc, withCompany, withAddress, withDocType, withSecondAddress);

            return (builder, record);
        }

        private static (LtmCustPayloadBuilder Builder, Entity Record, FakeOrganizationService Service)
            GivenContactWithService(
                string companyCode = DataAreaId,
                string? ruc = Ruc,
                bool withCompany = true,
                bool withAddress = true,
                bool withDocType = true,
                bool withSecondAddress = false)
        {
            var service   = new FakeOrganizationService();
            var contactId = Guid.NewGuid();

            var record = new Entity("contact", contactId);

            if (withCompany)
                record[LtmCustMapping.CompanyAttribute] = service.Add(
                    "cdm_company", Guid.NewGuid(),
                    (LtmCustMapping.CompanyCodeAttribute, companyCode));

            if (ruc is not null)
                record[LtmCustMapping.IdentificationNumberAttribute] = ruc;

            if (withDocType)
                // El lookup del contact apunta directo a la virtual entity, de cuya unica
                // fila salen los dos codigos.
                record[LtmCustMapping.DocTypeAttribute] = service.Add(
                    LtmCustMapping.VirtualDocTypeEntity, Guid.NewGuid(),
                    (LtmCustMapping.VirtualDocTypeId, "RUC"),
                    (LtmCustMapping.VirtualTaxPayerTypeId, "PJ"));

            // El grupo depende solo de la legal entity.
            service.Add(
                LtmCustMapping.VirtualAccountTypeGroupEntity, Guid.NewGuid(),
                (LtmCustMapping.VirtualAccountTypeGroupId, "Cliente Local"),
                (LtmCustMapping.VirtualAccountTypeGroupCompany, companyCode),
                (LtmCustMapping.VirtualAccountTypeGroupCustVend, LtmCustMapping.CustVendEntityCustomer));

            if (withAddress)
            {
                var country = service.Add(
                    LtmCustMapping.AddressCountryLookup, Guid.NewGuid(),
                    (LtmCustMapping.CountryCodeAttribute, "PRY"));

                var state = service.Add(
                    LtmCustMapping.AddressStateLookup, Guid.NewGuid(),
                    (LtmCustMapping.StateNameAttribute, "Asuncion"));

                service.Add(
                    LtmCustMapping.AddressEntity, Guid.NewGuid(),
                    (LtmCustMapping.AddressParentAttribute, new EntityReference("contact", contactId)),
                    (LtmCustMapping.AddressNumberAttribute, LtmCustMapping.PrimaryAddressNumber),
                    (LtmCustMapping.AddressCountryLookup, country),
                    (LtmCustMapping.AddressStateLookup, state));
            }

            if (withSecondAddress)
            {
                var otherCountry = service.Add(
                    LtmCustMapping.AddressCountryLookup, Guid.NewGuid(),
                    (LtmCustMapping.CountryCodeAttribute, "ARG"));

                service.Add(
                    LtmCustMapping.AddressEntity, Guid.NewGuid(),
                    (LtmCustMapping.AddressParentAttribute, new EntityReference("contact", contactId)),
                    (LtmCustMapping.AddressNumberAttribute, 2),
                    (LtmCustMapping.AddressCountryLookup, otherCountry));
            }

            var catalogs = new LtmCatalogResolver(
                service, new LtmCatalogCache(), NullLogger<LtmCatalogResolver>.Instance);

            var builder = new LtmCustPayloadBuilder(
                service,
                catalogs,
                LtmSchema(),
                NullLogger<LtmCustPayloadBuilder>.Instance);

            return (builder, record, service);
        }
    }
}
