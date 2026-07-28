using System.Net;
using Axxon.Eip.Core.FinOps;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// Clasificacion de errores de F&amp;O. Lo que esta en juego: un 400 tratado como
    /// transitorio se reintenta hasta agotar el delivery count, martillando F&amp;O sin
    /// ninguna chance de exito.
    ///
    /// Los cuerpos de abajo son los que devolvio F&amp;O de INTE el 2026-07-28.
    /// </summary>
    public class FoODataExceptionTests
    {
        // Grupo de clientes que no existe en la compania destino.
        private const string CustomerGroupBody = """
            {"error":{"code":"","message":"An error has occurred.","innererror":{
              "message":"Write failed for table row of type 'CustCustomerV3Entity'. Infolog: Warning: The value 'Local' in field 'Customer group' is not found in the related table 'Customer groups'.; Warning: validateField failed on field 'CustCustomerV3Entity.CustomerGroupId'.",
              "type":"Microsoft.Dynamics.Platform.Integration.Services.OData.AxODataWriteException",
              "stacktrace":"   at Microsoft.Dynamics.Platform.Integration.Services.OData.Update.UpdateProcessor.CreateEntity_Save(...)"}}}
            """;

        // El party ya existe en F&O como prospect.
        private const string ProspectBody = """
            {"error":{"code":"","message":"An error has occurred.","innererror":{
              "message":"Write failed for table row of type 'CustCustomerV3Entity'. Infolog: Error: Cannot create customer with account number 'CAUT-000761' with same party number as prospect 'CON-01757-D4W5'. The customer and prospect should have the same account number..",
              "type":"Microsoft.Dynamics.Platform.Integration.Services.OData.AxODataWriteException",
              "stacktrace":"   at Microsoft.Dynamics.Platform.Integration.Services.OData.Update.UpdateProcessor.CreateEntity_Save(...)"}}}
            """;

        // ── Clasificacion ─────────────────────────────────────────────

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]        // regla de negocio
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Conflict)]
        [InlineData(HttpStatusCode.UnprocessableEntity)]
        public void Un_4xx_de_negocio_es_permanente(HttpStatusCode status)
        {
            var ex = FoODataException.FromResponse(status, "CustomersV3", CustomerGroupBody);

            Assert.True(ex.IsPermanent);
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests)]   // ya lo reintenta el resilience handler
        [InlineData(HttpStatusCode.RequestTimeout)]
        [InlineData(HttpStatusCode.Unauthorized)]      // token vencido: un reintento puede resolverlo
        [InlineData(HttpStatusCode.Forbidden)]         // permiso que todavia no propago
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public void Un_error_transitorio_no_es_permanente(HttpStatusCode status)
        {
            var ex = FoODataException.FromResponse(status, "CustomersV3", "");

            Assert.False(ex.IsPermanent);
        }

        // ── Extraccion del mensaje de negocio ─────────────────────────

        [Fact]
        public void Extrae_el_infolog_de_fo_y_descarta_el_stacktrace()
        {
            var ex = FoODataException.FromResponse(
                HttpStatusCode.BadRequest, "CustomersV3", CustomerGroupBody);

            Assert.Contains("The value 'Local' in field 'Customer group' is not found", ex.FoMessage);
            Assert.DoesNotContain("stacktrace", ex.FoMessage);
            Assert.DoesNotContain("UpdateProcessor", ex.FoMessage);
        }

        [Fact]
        public void El_detalle_del_dlq_dice_que_paso_no_un_stacktrace()
        {
            var ex = FoODataException.FromResponse(
                HttpStatusCode.BadRequest, "CustomersV3", ProspectBody);

            Assert.Contains("same party number as prospect 'CON-01757-D4W5'", ex.Detail);
            Assert.DoesNotContain("Microsoft.Dynamics.Platform", ex.Detail);
        }

        [Fact]
        public void Ignora_el_mensaje_generico_de_afuera()
        {
            // "An error has occurred." no le sirve a nadie; el util esta en innererror.
            var ex = FoODataException.FromResponse(
                HttpStatusCode.BadRequest, "CustomersV3", CustomerGroupBody);

            Assert.NotEqual("An error has occurred.", ex.FoMessage);
        }

        [Fact]
        public void Sin_innererror_cae_al_mensaje_de_afuera()
        {
            var ex = FoODataException.FromResponse(
                HttpStatusCode.BadRequest, "CustomersV3",
                """{"error":{"code":"","message":"Entity set not found."}}""");

            Assert.Equal("Entity set not found.", ex.FoMessage);
        }

        [Fact]
        public void Una_respuesta_que_no_es_json_no_rompe()
        {
            // F&O no siempre devuelve JSON: puede caer una pagina de error del gateway.
            var ex = FoODataException.FromResponse(
                HttpStatusCode.BadGateway, "CustomersV3", "<html><body>502 Bad Gateway</body></html>");

            Assert.Null(ex.FoMessage);
            Assert.Contains("502 Bad Gateway", ex.Detail);
        }

        [Fact]
        public void El_detalle_se_trunca_para_entrar_en_el_dead_letter()
        {
            // Service Bus corta la descripcion del dead-letter en 4096 caracteres.
            var ex = FoODataException.FromResponse(
                HttpStatusCode.BadRequest, "CustomersV3", new string('x', 10_000));

            Assert.True(ex.Detail.Length <= 4000, $"Detail quedo en {ex.Detail.Length} caracteres.");
        }

        [Fact]
        public void Sigue_siendo_una_httprequestexception()
        {
            // El codigo que ya capturaba HttpRequestException no se entera del cambio.
            var ex = FoODataException.FromResponse(HttpStatusCode.BadRequest, "CustomersV3", "");

            Assert.IsAssignableFrom<HttpRequestException>(ex);
        }
    }
}
