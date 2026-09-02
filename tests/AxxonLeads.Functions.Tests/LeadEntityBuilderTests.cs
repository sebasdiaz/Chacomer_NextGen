using AxxonLeads.Functions.Configuration;
using AxxonLeads.Functions.Services;
using Microsoft.Xrm.Sdk;

namespace AxxonLeads.Functions.Tests
{
    /// <summary>
    /// El mapeo payload -&gt; atributos de <c>lead</c>. Es la parte que mas va a cambiar
    /// cuando el negocio pida un campo mas, asi que se prueba sin org.
    /// </summary>
    public class LeadEntityBuilderTests
    {
        private static Entity Build(
            Action<AxxonLeads.Functions.Models.LeadIntakePayload>? tweak = null,
            LeadIntakeOptions? options = null)
        {
            var payload = Given.Payload();
            tweak?.Invoke(payload);

            return new LeadEntityBuilder(options ?? Given.Options()).Build(payload);
        }

        [Fact]
        public void Escribe_la_entidad_lead_con_los_obligatorios()
        {
            var lead = Build();

            Assert.Equal(LeadEntityBuilder.LeadEntity, lead.LogicalName);
            Assert.Equal("Consulta por camion", lead["subject"]);
            Assert.Equal("Diaz", lead["lastname"]);
        }

        [Fact]
        public void El_ruc_va_a_la_columna_que_dice_el_app_setting()
        {
            var lead = Build(options: new LeadIntakeOptions { IdentificationAttribute = "axx_ruc" });

            Assert.Equal("80012345-6", lead["axx_ruc"]);
            Assert.False(lead.Contains(LeadIntakeOptions.DefaultIdentificationAttribute));
        }

        [Fact]
        public void Un_campo_que_no_viene_no_se_escribe()
        {
            // Distinto de escribirlo vacio: en un Create da igual, en un Update seria
            // borrar un dato que ya estaba.
            var lead = Build(p => p.FirstName = null);

            Assert.False(lead.Contains("firstname"));
        }

        [Fact]
        public void Un_campo_en_blanco_tampoco_se_escribe()
        {
            var lead = Build(p => p.EmailAddress1 = "   ");

            Assert.False(lead.Contains("emailaddress1"));
        }

        [Fact]
        public void Los_valores_se_recortan()
        {
            var lead = Build(p => p.CompanyName = "  Ferreteria San Blas S.A.  ");

            Assert.Equal("Ferreteria San Blas S.A.", lead["companyname"]);
        }

        [Fact]
        public void El_domicilio_va_a_los_campos_address1()
        {
            var lead = Build(p => p.Address = Given.Address());

            Assert.Equal("Casa central", lead["address1_name"]);
            Assert.Equal("Avda. Mariscal Lopez", lead["address1_line1"]);
            Assert.Equal("1234", lead["address1_line2"]);
            Assert.Equal("Piso 3", lead["address1_line3"]);
            Assert.Equal("Asuncion", lead["address1_city"]);
            Assert.Equal("Central", lead["address1_stateorprovince"]);
            Assert.Equal("1209", lead["address1_postalcode"]);
            Assert.Equal("Paraguay", lead["address1_country"]);
            Assert.Equal("021 555 000", lead["address1_telephone1"]);
        }

        [Fact]
        public void Sin_domicilio_no_se_toca_ningun_campo_de_direccion()
        {
            var lead = Build(p => p.Address = null);

            Assert.DoesNotContain(lead.Attributes.Keys, k => k.StartsWith("address1_"));
        }

        [Fact]
        public void Un_domicilio_parcial_solo_escribe_lo_que_vino()
        {
            var lead = Build(p => p.Address = new AxxonLeads.Functions.Models.LeadAddress
            {
                City    = "Asuncion",
                Country = "Paraguay"
            });

            Assert.Equal("Asuncion", lead["address1_city"]);
            Assert.False(lead.Contains("address1_line1"));
        }

        [Fact]
        public void El_origen_viaja_como_optionset()
        {
            var lead = Build(p => p.LeadSourceCode = 3);

            Assert.Equal(3, Assert.IsType<OptionSetValue>(lead["leadsourcecode"]).Value);
        }

        [Fact]
        public void Sin_columna_de_id_externo_el_externalId_no_se_escribe()
        {
            // Deduplicacion apagada: no hay donde guardarlo, y escribirlo en una columna
            // inexistente voltearia el Create entero.
            var lead = Build(p => p.ExternalId = "TC-99321");

            Assert.False(lead.Contains(Given.ExternalIdAttribute));
        }

        [Fact]
        public void Con_columna_de_id_externo_el_externalId_se_sella()
        {
            var lead = Build(p => p.ExternalId = "TC-99321", Given.OptionsWithDedup());

            Assert.Equal("TC-99321", lead[Given.ExternalIdAttribute]);
        }
    }
}
