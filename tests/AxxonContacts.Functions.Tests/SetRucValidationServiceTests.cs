using System.Net;
using System.Text;
using Axxon.Eip.Core.Fiscal;
using AxxonContacts.Functions.Services;
using AxxonContacts.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonContacts.Functions.Tests
{
    /// <summary>
    /// axx_fiscalstate es un <b>Picklist</b> (single-select) en account y en contact — lo
    /// confirma la metadata de las dos entidades. Hasta el 2026-09-04 el contact se escribia
    /// como OptionSetValueCollection: Dataverse respondia "Incorrect attribute value type",
    /// el catch del servicio lo degradaba a warning y el estado fiscal del master nunca se
    /// actualizaba, sin que fallara nada visible.
    ///
    /// El tipo del atributo es justamente lo que no se ve al leer el codigo ni al correr el
    /// flujo, asi que se fija aca.
    /// </summary>
    public class SetRucValidationServiceTests
    {
        private const string RespuestaActivo = """
            { "codigo": "0", "contribuyente": { "estado": "ACTIVO", "razonSocial": "RODAS MELISSA" } }
            """;

        private static SetRucValidationService Servicio(
            FakeUpdateOrganizationService orgService,
            string respuesta = RespuestaActivo)
        {
            var http = new HttpClient(new RespuestaFijaHandler(respuesta))
            {
                BaseAddress = new Uri("https://servicios.set.gov.py/EsetApiWS/ApiWS/")
            };

            var setApi = new SetApiService(http, new SetApiOptions { ApiKey = "k" }, NullLogger.Instance);

            return new SetRucValidationService(setApi, orgService, NullLogger.Instance);
        }

        [Theory]
        [InlineData("contact")]
        [InlineData("account")]
        public async Task El_estado_fiscal_se_escribe_como_OptionSetValue(string entidad)
        {
            var orgService = new FakeUpdateOrganizationService();
            var masterId   = Guid.NewGuid();

            await Servicio(orgService).ValidateAndUpdateAsync(entidad, masterId, "4197845-5");

            var actualizado = Assert.Single(orgService.Updates);
            Assert.Equal(entidad, actualizado.LogicalName);
            Assert.Equal(masterId, actualizado.Id);

            var estado = Assert.IsType<OptionSetValue>(actualizado["axx_fiscalstate"]);
            Assert.Equal(1, estado.Value); // ACTIVO
        }

        [Theory]
        [InlineData("contact")]
        [InlineData("account")]
        public async Task El_json_crudo_de_la_SET_queda_en_axx_dnitresponse(string entidad)
        {
            var orgService = new FakeUpdateOrganizationService();

            await Servicio(orgService).ValidateAndUpdateAsync(entidad, Guid.NewGuid(), "4197845-5");

            var actualizado = Assert.Single(orgService.Updates);
            Assert.Contains("RODAS MELISSA", Assert.IsType<string>(actualizado["axx_dnitresponse"]));
        }

        /// <summary>
        /// Un estado que no esta en el mapeo no tiene que romper ni escribir basura: se
        /// guarda el response crudo y axx_fiscalstate se deja como estaba.
        /// </summary>
        [Fact]
        public async Task Un_estado_desconocido_no_escribe_el_campo()
        {
            var orgService = new FakeUpdateOrganizationService();
            var respuesta  = """
                { "codigo": "0", "contribuyente": { "estado": "ESTADO QUE NO EXISTE" } }
                """;

            await Servicio(orgService, respuesta).ValidateAndUpdateAsync("contact", Guid.NewGuid(), "4197845-5");

            var actualizado = Assert.Single(orgService.Updates);
            Assert.False(actualizado.Contains("axx_fiscalstate"));
        }

        private sealed class RespuestaFijaHandler(string cuerpo) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
                });
        }
    }
}
