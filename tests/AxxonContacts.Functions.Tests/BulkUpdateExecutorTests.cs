using AxxonContacts.Functions.Services;
using AxxonContacts.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace AxxonContacts.Functions.Tests
{
    /// <summary>
    /// El 13/08/2026 un contacto quedo sin linkear a su master: el update del link
    /// disparo el plugin sincronico de Dual Write, F&amp;O estaba throttleando y el
    /// ExecuteMultiple con ContinueOnError se comio el fallo. El mensaje se completo
    /// como exitoso y el link se perdio para siempre.
    ///
    /// Estos tests fijan las dos mitades del arreglo: que lo que falla se reintente,
    /// y que lo que sigue fallando termine lanzando en vez de desaparecer.
    /// </summary>
    public class BulkUpdateExecutorTests
    {
        // Sin espera entre reintentos: el backoff real no aporta nada a los tests.
        private static readonly TimeSpan[] SinEspera = [TimeSpan.Zero, TimeSpan.Zero];

        private static Entity Raw(string nombre) => new("contact", Guid.NewGuid()) { ["name"] = nombre };

        private static Task Ejecutar(FakeBulkOrganizationService crm, params Entity[] updates) =>
            BulkUpdateExecutor.ExecuteAsync(
                crm, NullLogger.Instance, "[Test]", updates, SinEspera);

        [Fact]
        public async Task Si_todos_pasan_no_hay_reintento()
        {
            var crm = new FakeBulkOrganizationService((_, _) => false);

            await Ejecutar(crm, Raw("uno"), Raw("dos"));

            Assert.Single(crm.Batches);
            Assert.Equal(2, crm.Batches[0].Count);
        }

        [Fact]
        public async Task El_reintento_manda_solo_los_updates_que_fallaron()
        {
            var pasa  = Raw("pasa");
            var falla = Raw("falla");

            // Throttling de un solo update, y solo en el primer intento.
            var crm = new FakeBulkOrganizationService(
                (attempt, entity) => attempt == 1 && entity.Id == falla.Id);

            await Ejecutar(crm, pasa, falla);

            Assert.Equal(2, crm.Batches.Count);
            Assert.Equal(falla.Id, Assert.Single(crm.Batches[1]).Id);
        }

        [Fact]
        public async Task Si_sigue_fallando_lanza_para_que_el_mensaje_se_reintente()
        {
            var crm = new FakeBulkOrganizationService((_, _) => true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Ejecutar(crm, Raw("uno")));

            // Un intento inicial + un reintento por cada delay configurado.
            Assert.Equal(3, crm.Batches.Count);

            // El motivo real tiene que viajar en el mensaje: es lo unico que va a
            // quedar visible cuando el mensaje caiga al DLQ.
            Assert.Contains("Dual Write core application error", ex.Message);
        }

        [Fact]
        public async Task Sin_updates_no_se_llama_a_Dataverse()
        {
            var crm = new FakeBulkOrganizationService((_, _) => true);

            await Ejecutar(crm);

            Assert.Empty(crm.Batches);
        }
    }
}
