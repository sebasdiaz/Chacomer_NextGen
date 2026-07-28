using AxxonCustomers.Functions.Mapping;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// Guarda contra el drift de los mapeos que se deployan.
    ///
    /// El export de Dual Write lo re-exporta el funcional y se reemplaza entero: estos
    /// tests son los que avisan si el re-export cambio algo que nos importa. Que fallen
    /// no significa que este mal el export — significa que hay que mirarlo.
    /// </summary>
    public class ShippedMappingsTests
    {
        private static readonly EntityMapRegistry Registry =
            EntityMapRegistry.Load(EntityMapRegistry.DefaultDirectory, NullLogger.Instance);

        private static EntityMap Contact => Registry.Get("contact");
        private static EntityMap Account => Registry.Get("account");

        [Fact]
        public void Los_mapeos_que_se_deployan_compilan()
        {
            // Redundante con el resto, pero es el test que quiere ver alguien que rompio
            // un JSON: dice exactamente eso y nada mas.
            Assert.NotEmpty(Registry.All);
        }

        // ── contact ───────────────────────────────────────────────────

        [Fact]
        public void Contact_escribe_en_customersv3_con_write_back_a_msdyn_contactpersonid()
        {
            Assert.Equal("CustomersV3", Contact.EntitySet);
            Assert.Equal("msdyn_contactpersonid", Contact.WriteBackAttribute);
            Assert.Equal("CUSTOMERACCOUNT", Contact.WriteBackTargetField);
            Assert.True(Contact.Field("CUSTOMERACCOUNT").ExcludeFromCreate);
        }

        [Fact]
        public void Contact_manda_partytype_person_como_constante()
        {
            // La fila PARTYTYPE -> msdyn_sellable del export no es invertible: se ignora
            // y PartyType pasa a ser constante.
            var partyType = Contact.Field("PartyType");

            Assert.Equal(FieldKind.Const, partyType.Kind);
            Assert.Equal("Person", partyType.ConstantValue);
            Assert.DoesNotContain(Contact.Fields, f => f.Attribute == "msdyn_sellable");
        }

        [Fact]
        public void Contact_nace_sellable()
        {
            // A365Sellable = Yes es lo que le dice a F&O que el registro es un contact.
            // No viene del export: es una decision de negocio en el overlay.
            var sellable = Contact.Field("A365Sellable");

            Assert.Equal(FieldKind.Const, sellable.Kind);
            Assert.Equal("Yes", sellable.ConstantValue);
        }

        [Fact]
        public void Contact_no_manda_los_campos_de_credito()
        {
            // Decision explicita: el export es la fuente de verdad y no los trae.
            // Los campos de credito viven en account. Si un re-export los incorpora,
            // este test cae y hay que decidir de nuevo.
            Assert.DoesNotContain(Contact.Fields, f => f.TargetField is "CREDITLIMIT" or "CREDITRATING");
            Assert.DoesNotContain(Contact.Fields, f => f.TargetField is "ONHOLDSTATUS" or "CREDMANNOTES");
        }

        [Fact]
        public void Contact_no_tiene_guarda_de_sincronizacion()
        {
            // Hoy este mapeo lo consume QualifyLead, que nunca tuvo guarda. Cuando entre
            // fo-sync hay que activar msdyn_sellable eq true y actualizar este test.
            Assert.Empty(Contact.SyncWhen);
        }

        // ── account ───────────────────────────────────────────────────

        [Fact]
        public void Account_hace_write_back_al_accountnumber()
        {
            Assert.Equal("accountnumber", Account.WriteBackAttribute);
            Assert.True(Account.Field("CUSTOMERACCOUNT").ExcludeFromCreate);
        }

        [Fact]
        public void Account_solo_sincroniza_organizaciones()
        {
            var condition = Assert.Single(Account.SyncWhen);

            Assert.Equal("customertypecode", condition.Attribute);
            Assert.Equal("3", condition.ExpectedValue);
        }

        [Fact]
        public void Account_manda_partytype_organization()
        {
            Assert.Equal("Organization", Account.Field("PartyType").ValueMap!["3"]);
        }

        [Fact]
        public void Account_traduce_el_onholdstatus_a_los_literales_del_enum_de_fo()
        {
            var map = Account.Field("ONHOLDSTATUS").ValueMap!;

            Assert.Equal("No",          map["806380000"]);
            Assert.Equal("Invoice",     map["806380001"]);
            Assert.Equal("All",         map["806380002"]);
            Assert.Equal("Payment",     map["806380003"]);
            Assert.Equal("Requisition", map["806380004"]);
            Assert.Equal("Never",       map["806380005"]);
        }

        // ── El bug que nos costo encontrar ────────────────────────────

        [Fact]
        public void Ningun_literal_de_enum_viaja_en_minuscula()
        {
            // El export escribe los literales en minuscula porque en direccion AX -> CRM
            // el destino es un int de OptionSet y el case da igual. En la nuestra el
            // destino es un enum de la API OData, que es case-sensitive.
            //
            // Si este test cae despues de un re-export: el overlay necesita el override
            // de case para ese campo.
            var offenders = Registry.All
                .SelectMany(map => map.Fields
                    .Where(f => f.ValueMap is not null)
                    .SelectMany(f => f.ValueMap!.Values.Select(v => (map.Name, f.TargetField, Value: v))))
                .Where(x => x.Value.Length > 0 && char.IsLower(x.Value[0]))
                .ToList();

            Assert.True(
                offenders.Count == 0,
                "Literales en minuscula hacia F&O: " +
                string.Join(", ", offenders.Select(o => $"{o.Name}.{o.TargetField}='{o.Value}'")));
        }

        // ── El payload de verdad ──────────────────────────────────────

        [Fact]
        public async Task Un_contact_completo_arma_el_payload_esperado()
        {
            var crm       = new FakeOrganizationService();
            var contactId = Guid.NewGuid();

            var company = crm.Add("cdm_company", Guid.NewGuid(), ("cdm_companycode", "cha"));
            var party   = crm.Add("msdyn_party", Guid.NewGuid(), ("msdyn_partynumber", "PARTY-77"));
            var group   = crm.Add("msdyn_customergroup", Guid.NewGuid(), ("msdyn_groupid", "MAY"));
            var currency = crm.Add("transactioncurrency", Guid.NewGuid(), ("isocurrencycode", "PYG"));

            var contact = new Entity("contact", contactId)
            {
                ["msdyn_company"]             = company,
                ["msdyn_partyid"]             = party,
                ["msdyn_customergroupid"]     = group,
                ["transactioncurrencyid"]     = currency,
                ["msdyn_identificationnumber"] = "80012345-6",
                ["msdyn_partycountry"]        = "PRY",
                ["description"]               = "Cliente de prueba",
                ["msdyn_contactpersonid"]     = "C-000999"
            };

            var schema = new FakeFoSchemaProvider(
                "dataAreaId", "PartyType", "A365Sellable", "PartyNumber", "CustomerGroupId",
                "SalesCurrencyCode", "IdentificationNumber", "PartyCountry", "PartyState",
                "SalesMemo", "PaymentDay", "PaymentSchedule", "PaymentMethod", "SalesTaxGroup",
                "PaymentTerms", "ContactPersonId", "CustomerAccount");

            var builder = new FoPayloadBuilder(crm, schema, NullLogger<FoPayloadBuilder>.Instance);
            var payload = await builder.BuildAsync(contact, Contact);

            Assert.Equal("cha", payload.DataAreaId);

            Assert.Equal(new Dictionary<string, object?>
            {
                ["dataAreaId"]           = "cha",
                ["PartyType"]            = "Person",
                ["A365Sellable"]         = "Yes",
                ["PartyNumber"]          = "PARTY-77",
                ["CustomerGroupId"]      = "MAY",
                ["SalesCurrencyCode"]    = "PYG",
                ["IdentificationNumber"] = "80012345-6",
                ["PartyCountry"]         = "PRY",
                ["SalesMemo"]            = "Cliente de prueba"
            }, payload.Fields);

            // El CustomerAccount no viaja (lo genera F&O) pero alimenta la idempotencia.
            Assert.Equal("C-000999", payload.MatchValues["CUSTOMERACCOUNT"]);
            Assert.Equal("PARTY-77", payload.MatchValues["PARTYNUMBER"]);
        }
    }
}
