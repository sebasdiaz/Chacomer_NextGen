using AxxonLeads.Functions.Configuration;
using AxxonLeads.Functions.Services;
using AxxonLeads.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AxxonLeads.Functions.Tests
{
    /// <summary>
    /// La deduplicacion, que es lo que hace segura la reentrega: Service Bus es
    /// at-least-once, y sin buscar antes cada reentrega seria un lead duplicado.
    /// </summary>
    public class LeadIntakeServiceTests
    {
        private static LeadIntakeService Service(
            FakeLeadOrganizationService org,
            LeadIntakeOptions options) =>
            new(org, new LeadEntityBuilder(options), options, NullLogger<LeadIntakeService>.Instance);

        [Fact]
        public async Task Crea_el_lead_cuando_no_existe()
        {
            var org     = new FakeLeadOrganizationService();
            var payload = Given.Payload();
            payload.ExternalId = "TC-99321";

            var result = await Service(org, Given.OptionsWithDedup()).ProcessAsync("thinkchat", payload);

            Assert.False(result.AlreadyExisted);
            Assert.Single(org.Creates);
            Assert.Equal("TC-99321", org.Creates[0][Given.ExternalIdAttribute]);
        }

        [Fact]
        public async Task Una_reentrega_no_crea_un_segundo_lead()
        {
            var org        = new FakeLeadOrganizationService();
            var existingId = org.Add(LeadEntityBuilder.LeadEntity, (Given.ExternalIdAttribute, "TC-99321"));

            var payload = Given.Payload();
            payload.ExternalId = "TC-99321";

            var result = await Service(org, Given.OptionsWithDedup()).ProcessAsync("thinkchat", payload);

            Assert.True(result.AlreadyExisted);
            Assert.Equal(existingId, result.LeadId);
            Assert.Empty(org.Creates);
        }

        [Fact]
        public async Task Un_externalId_distinto_si_crea()
        {
            var org = new FakeLeadOrganizationService();
            org.Add(LeadEntityBuilder.LeadEntity, (Given.ExternalIdAttribute, "TC-11111"));

            var payload = Given.Payload();
            payload.ExternalId = "TC-99321";

            var result = await Service(org, Given.OptionsWithDedup()).ProcessAsync("thinkchat", payload);

            Assert.False(result.AlreadyExisted);
            Assert.Single(org.Creates);
        }

        [Fact]
        public async Task Sin_columna_de_id_externo_no_se_consulta_y_se_crea_igual()
        {
            // Deduplicacion apagada: no hay por donde buscar. Consultar de todos modos
            // seria un viaje de red por mensaje que no puede encontrar nada.
            var org     = new FakeLeadOrganizationService();
            var payload = Given.Payload();
            payload.ExternalId = "TC-99321";

            var result = await Service(org, Given.Options()).ProcessAsync("thinkchat", payload);

            Assert.False(result.AlreadyExisted);
            Assert.Empty(org.Queries);
            Assert.Single(org.Creates);
        }

        [Fact]
        public async Task Un_mensaje_sin_externalId_no_se_consulta_y_se_crea()
        {
            var org = new FakeLeadOrganizationService();

            var result = await Service(org, Given.OptionsWithDedup()).ProcessAsync("thinkchat", Given.Payload());

            Assert.False(result.AlreadyExisted);
            Assert.Empty(org.Queries);
            Assert.Single(org.Creates);
        }
    }
}
