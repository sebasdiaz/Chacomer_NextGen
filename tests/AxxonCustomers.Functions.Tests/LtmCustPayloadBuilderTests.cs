using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Models;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El mapeo hacia LTMCustTable. A diferencia del de CustomersV3 no se declara en JSON
    /// (consulta con filtro, sale a una relacion 1:N y valida contra un catalogo del ERP), asi
    /// que las decisiones que en aquel afirma <c>ShippedMappingsTests</c> sobre los archivos,
    /// aca se afirman sobre el codigo.
    ///
    /// Lo que se afirma es el alcance funcional de la v1: documento RUC, pais Paraguay, tipo
    /// de contribuyente por tipo de registro, y solo las legal entities que tienen la
    /// localizacion configurada en el ERP.
    /// </summary>
    public class LtmCustPayloadBuilderTests
    {
        private const string DataAreaId = "caut";
        private const string AccountNum = "CAUT-000012";
        private const string Ruc        = "80098873-6";
        private const string State      = "DPTO_11";

        [Fact]
        public async Task Mapea_el_alcance_funcional()
        {
            var (builder, record) = GivenContact();

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.Equal(DataAreaId, payload.Fields[LtmCustMapping.DataAreaId]);
            Assert.Equal(AccountNum, payload.Fields[LtmCustMapping.AccountNum]);
            Assert.Equal("RUC",      payload.Fields[LtmCustMapping.CountryDocTypeId]);
            Assert.Equal("PN",       payload.Fields[LtmCustMapping.TaxPayerTypeId]);
            Assert.Equal("Cliente Local", payload.Fields[LtmCustMapping.AccountTypeGroupId]);
            Assert.Equal("PRY",      payload.Fields[LtmCustMapping.CountryRegionId]);
            Assert.Equal(State,      payload.Fields[LtmCustMapping.StateId]);
        }

        [Fact]
        public async Task El_documento_y_el_pais_son_constantes_del_alcance()
        {
            // No se leen del cliente: el alcance definido hoy es RUC y Paraguay. El lookup de
            // tipo de documento que suponia el analisis funcional no existe (la virtual entity
            // no tiene ninguna relacion 1:N), y el pais de la direccion viene como "PY", que no
            // es el codigo que conoce F&O.
            var (builder, record) = GivenContact();

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.Equal(LtmCustMapping.CountryDocTypeRuc,     payload.Fields[LtmCustMapping.CountryDocTypeId]);
            Assert.Equal(LtmCustMapping.CountryRegionParaguay, payload.Fields[LtmCustMapping.CountryRegionId]);
        }

        [Fact]
        public async Task El_ruc_alimenta_los_dos_campos_de_documento()
        {
            var (builder, record) = GivenContact();

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            // CountryDocNum y StateDocNum salen los dos de msdyn_identificationnumber: es el
            // unico atributo de CRM que alimenta dos campos de F&O.
            Assert.NotNull(payload);
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
            var (builder, record) = GivenContact(companyCode: "chac");

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.Equal("chac", payload.Fields[LtmCustMapping.DataAreaId]);
        }

        [Fact]
        public async Task Sin_company_no_se_puede_resolver_la_legal_entity()
        {
            var (builder, record) = GivenContact(withCompany: false);

            await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => builder.BuildAsync(record, LtmCustSource.Contact, AccountNum));
        }

        // ── La guarda de alcance ──────────────────────────────────────

        [Fact]
        public async Task Una_legal_entity_sin_localizacion_PY_no_se_mapea()
        {
            // Es la guarda de alcance, y se deriva del ERP: sin filas de documento para la
            // company no hay nada que escribir. En INTE mas de la mitad de los clientes
            // sellable viven en legal entities asi (las de USA y Alemania), y a la cola llegan
            // igual.
            var (builder, record) = GivenContact(taxPayerTypes: []);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.Null(payload);
        }

        [Fact]
        public async Task Una_company_fuera_de_alcance_no_consulta_el_resto_de_los_catalogos()
        {
            // Cada consulta a una virtual entity es un viaje a F&O: si la company esta fuera
            // de alcance no tiene sentido pagar el del grupo de cliente.
            var (builder, record, org, _) = GivenContactWithFakes(taxPayerTypes: []);

            await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.DoesNotContain(
                LtmCustMapping.VirtualAccountTypeGroupEntity, org.RetrieveMultiples);
        }

        // ── Tipo de contribuyente ─────────────────────────────────────

        [Fact]
        public async Task El_tipo_de_contribuyente_sale_del_tipo_de_registro()
        {
            // Misma decision que los overlays de CustomersV3 con PartyType: contact es persona
            // fisica y account persona juridica. axx_tipopersoneriajuridica seria mas fiel al
            // dato, pero esta vacio en la mayoria de los clientes.
            var (contactBuilder, contact) = GivenContact();
            var (accountBuilder, account) = GivenAccount();

            var contactPayload = await contactBuilder.BuildAsync(contact, LtmCustSource.Contact, AccountNum);
            var accountPayload = await accountBuilder.BuildAsync(account, LtmCustSource.Account, AccountNum);

            Assert.NotNull(contactPayload);
            Assert.NotNull(accountPayload);
            Assert.Equal("PN", contactPayload.Fields[LtmCustMapping.TaxPayerTypeId]);
            Assert.Equal("PJ", accountPayload.Fields[LtmCustMapping.TaxPayerTypeId]);
        }

        [Fact]
        public async Task Un_tipo_de_contribuyente_que_la_company_no_tiene_se_omite()
        {
            // La company tiene la localizacion configurada, pero no la combinacion RUC + PN.
            // Se omite el campo en vez de mandar un codigo que F&O va a rechazar con un 400.
            var (builder, record) = GivenContact(taxPayerTypes: ["PJ"]);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.TaxPayerTypeId));
            Assert.Equal(Ruc, payload.Fields[LtmCustMapping.CountryDocNum]);
        }

        // ── Grupo de cliente ──────────────────────────────────────────

        [Fact]
        public async Task Si_la_company_tiene_varios_grupos_de_cliente_no_se_adivina()
        {
            // caut tiene dos en INTE ("Cliente Local" y "Cliente Exterior") y ningun criterio
            // del repo puede elegir: ordenarlos alfabeticamente elegiria el de exterior para
            // clientes locales. Se omite el campo, F&O aplica su default.
            var (builder, record) = GivenContact(
                accountTypeGroups: ["Cliente Local", "Cliente Exterior"]);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.AccountTypeGroupId));
        }

        [Fact]
        public async Task Sin_grupo_de_cliente_el_resto_del_payload_viaja_igual()
        {
            var (builder, record) = GivenContact(accountTypeGroups: []);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.AccountTypeGroupId));
            Assert.Equal("PN", payload.Fields[LtmCustMapping.TaxPayerTypeId]);
        }

        // ── Estado ────────────────────────────────────────────────────

        [Fact]
        public async Task Se_usa_la_direccion_que_tiene_estado_y_no_la_numero_uno()
        {
            // Dataverse crea automaticamente las direcciones 1 y 2 y casi nunca se completan:
            // las cargadas a mano arrancan en la 3. Filtrar por addressnumber = 1, como hacia
            // la version anterior, apuntaba justo a la fila vacia.
            var (builder, record) = GivenContact(withEmptyFirstAddress: true);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.Equal(State, payload.Fields[LtmCustMapping.StateId]);
        }

        [Fact]
        public async Task Un_estado_que_no_existe_en_el_catalogo_de_FO_se_omite()
        {
            // stateorprovince es texto libre y esta sucio: conviven DPTO_11, Central, CEN y
            // hasta BA. Un estado que F&O no conoce se rechaza con un 400 que manda al DLQ la
            // fila entera, por un campo que no es el objetivo de la integracion.
            var (builder, record) = GivenContact(state: "BA");

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.StateId));
            Assert.Equal("PRY", payload.Fields[LtmCustMapping.CountryRegionId]);
        }

        [Fact]
        public async Task El_estado_viaja_con_la_grafia_del_ERP()
        {
            // La comparacion es case-insensitive pero el destino no: se manda el codigo tal
            // como lo escribe F&O, no como vino de CRM.
            var (builder, record) = GivenContact(state: "dpto_11");

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.Equal(State, payload.Fields[LtmCustMapping.StateId]);
        }

        [Fact]
        public async Task Sin_direccion_con_estado_se_omite_el_estado_pero_el_resto_viaja()
        {
            var (builder, record) = GivenContact(state: null);

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.StateId));
            Assert.Equal("PRY", payload.Fields[LtmCustMapping.CountryRegionId]);
            Assert.Equal(Ruc,   payload.Fields[LtmCustMapping.CountryDocNum]);
        }

        // ── Generales ─────────────────────────────────────────────────

        [Fact]
        public async Task Los_campos_vacios_se_omiten()
        {
            // Vaciar un campo en CRM no lo vacia en F&O: el mapeo no sabe distinguir "el
            // usuario borro el dato" de "nunca se completo". Mismo criterio que CustomersV3.
            var (builder, record) = GivenContact(ruc: "   ");

            var payload = await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.NotNull(payload);
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.CountryDocNum));
            Assert.False(payload.Fields.ContainsKey(LtmCustMapping.StateDocNum));
        }

        [Fact]
        public async Task Los_catalogos_se_leen_una_sola_vez()
        {
            // Cada consulta a una virtual entity es Dataverse llamando en vivo a F&O, y el
            // catalogo de estados es un viaje mas al ERP: sin cache cada mensaje los pagaria
            // de nuevo.
            var (builder, record, org, fo) = GivenContactWithFakes();

            await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);
            await builder.BuildAsync(record, LtmCustSource.Contact, AccountNum);

            Assert.Equal(1, org.RetrieveMultiples.Count(e => e == LtmCustMapping.VirtualDocTypeEntity));
            Assert.Equal(1, org.RetrieveMultiples.Count(e => e == LtmCustMapping.VirtualAccountTypeGroupEntity));
            Assert.Single(fo.Queries);
        }

        [Fact]
        public void Contact_y_account_difieren_en_el_AccountNum_y_el_tipo_de_contribuyente()
        {
            Assert.Equal("msdyn_contactpersonid", LtmCustSource.Contact.AccountNumberAttribute);
            Assert.Equal("accountnumber",         LtmCustSource.Account.AccountNumberAttribute);
            Assert.Equal("PN", LtmCustSource.Contact.TaxPayerTypeId);
            Assert.Equal("PJ", LtmCustSource.Account.TaxPayerTypeId);

            // Con el tipo de documento constante, el unico atributo que se llamaba distinto en
            // cada entidad (axx_tipodocumento / axx_tipodedocumento) salio del mapeo: las
            // columnas del Retrieve solo se diferencian en el write-back.
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
            var (_, record, org, fo) = GivenContactWithFakes();

            var builder = new LtmCustPayloadBuilder(
                org,
                new LtmCatalogResolver(
                    org, fo, new LtmCatalogCache(), NullLogger<LtmCatalogResolver>.Instance),
                new FakeFoSchemaProvider(LtmCustMapping.DataAreaId),
                NullLogger<LtmCustPayloadBuilder>.Instance);

            await Assert.ThrowsAsync<NonRetryableSyncException>(
                () => builder.BuildAsync(record, LtmCustSource.Contact, AccountNum));
        }

        // ── Armado ────────────────────────────────────────────────────

        /// <summary>Las propiedades de LTMCustTable, con el casing del $metadata de F&amp;O.</summary>
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
            string[]? taxPayerTypes = null,
            string[]? accountTypeGroups = null,
            string? state = State,
            bool withEmptyFirstAddress = false)
        {
            var (builder, record, _, _) = GivenWithFakes(
                LtmCustSource.Contact, companyCode, ruc, withCompany,
                taxPayerTypes, accountTypeGroups, state, withEmptyFirstAddress);

            return (builder, record);
        }

        private static (LtmCustPayloadBuilder Builder, Entity Record) GivenAccount()
        {
            var (builder, record, _, _) = GivenWithFakes(LtmCustSource.Account);
            return (builder, record);
        }

        private static (LtmCustPayloadBuilder Builder, Entity Record, FakeOrganizationService Org,
            FakeFoODataClient Fo) GivenContactWithFakes(
                string[]? taxPayerTypes = null)
            => GivenWithFakes(LtmCustSource.Contact, taxPayerTypes: taxPayerTypes);

        private static (LtmCustPayloadBuilder Builder, Entity Record, FakeOrganizationService Org,
            FakeFoODataClient Fo) GivenWithFakes(
                LtmCustSource source,
                string companyCode = DataAreaId,
                string? ruc = Ruc,
                bool withCompany = true,
                string[]? taxPayerTypes = null,
                string[]? accountTypeGroups = null,
                string? state = State,
                bool withEmptyFirstAddress = false)
        {
            var org      = new FakeOrganizationService();
            var recordId = Guid.NewGuid();

            var record = new Entity(source.EntityLogicalName, recordId);

            if (withCompany)
                record[LtmCustMapping.CompanyAttribute] = org.Add(
                    "cdm_company", Guid.NewGuid(),
                    (LtmCustMapping.CompanyCodeAttribute, companyCode));

            if (ruc is not null)
                record[LtmCustMapping.IdentificationNumberAttribute] = ruc;

            // Filas de documento de la company: son la guarda de alcance y la fuente del tipo
            // de contribuyente.
            foreach (var taxPayerType in taxPayerTypes ?? ["PN", "PJ"])
                org.Add(
                    LtmCustMapping.VirtualDocTypeEntity, Guid.NewGuid(),
                    (LtmCustMapping.VirtualDocTypeCompany, companyCode),
                    (LtmCustMapping.VirtualDocTypeId, LtmCustMapping.CountryDocTypeRuc),
                    (LtmCustMapping.VirtualTaxPayerTypeId, taxPayerType));

            // El grupo depende solo de la legal entity.
            foreach (var group in accountTypeGroups ?? ["Cliente Local"])
                org.Add(
                    LtmCustMapping.VirtualAccountTypeGroupEntity, Guid.NewGuid(),
                    (LtmCustMapping.VirtualAccountTypeGroupId, group),
                    (LtmCustMapping.VirtualAccountTypeGroupCompany, companyCode),
                    (LtmCustMapping.VirtualAccountTypeGroupCustVend,
                        new OptionSetValue(LtmCustMapping.CustVendEntityCustomer)));

            // Las que crea Dataverse solo, sin ningun dato cargado.
            if (withEmptyFirstAddress)
                org.Add(
                    LtmCustMapping.AddressEntity, Guid.NewGuid(),
                    (LtmCustMapping.AddressParentAttribute,
                        new EntityReference(source.EntityLogicalName, recordId)),
                    (LtmCustMapping.AddressNumberAttribute, 1));

            if (state is not null)
                org.Add(
                    LtmCustMapping.AddressEntity, Guid.NewGuid(),
                    (LtmCustMapping.AddressParentAttribute,
                        new EntityReference(source.EntityLogicalName, recordId)),
                    (LtmCustMapping.AddressNumberAttribute, 3),
                    (LtmCustMapping.AddressStateAttribute, state));

            // El catalogo de estados de Paraguay, tal como lo devuelve F&O.
            var fo = new FakeFoODataClient(State, "ASU", "Central");

            var catalogs = new LtmCatalogResolver(
                org, fo, new LtmCatalogCache(), NullLogger<LtmCatalogResolver>.Instance);

            var builder = new LtmCustPayloadBuilder(
                org, catalogs, LtmSchema(), NullLogger<LtmCustPayloadBuilder>.Instance);

            return (builder, record, org, fo);
        }
    }
}
