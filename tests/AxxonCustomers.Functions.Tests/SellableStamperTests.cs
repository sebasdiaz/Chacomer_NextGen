using AxxonCustomers.Functions.Configuration;
using AxxonCustomers.Functions.Services;
using AxxonCustomers.Functions.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// El sellado de <c>msdyn_sellable</c> al calificar es lo unico que hace que un
    /// prospecto recien calificado pase la guarda <c>syncWhen</c> del mapeo del contact.
    /// Lo que fijan estos tests: que escriba el valor configurado y no otro, que sin
    /// setting no toque el registro (apagado = conducta historica), y que un valor basura
    /// en el App Setting no se lea como <c>false</c> — eso dejaria de sincronizar todo.
    /// </summary>
    public class SellableStamperTests
    {
        private static readonly Guid ContactId = Guid.NewGuid();

        private readonly FakeOrganizationService _crm = new();

        private SellableStamper Stamper(bool? configured) =>
            new(_crm,
                new AppSettings { QualifyLeadSellableValue = configured },
                NullLogger<SellableStamper>.Instance);

        [Fact]
        public void Sella_el_contact_con_el_valor_configurado()
        {
            var stamped = Stamper(true).Stamp(ContactId);

            Assert.True(stamped);

            var update = Assert.Single(_crm.Updates);
            Assert.Equal("contact", update.LogicalName);
            Assert.Equal(ContactId, update.Id);
            Assert.True(update.GetAttributeValue<bool>(SellableStamper.SellableAttribute));
        }

        [Fact]
        public void El_valor_sale_del_setting_no_es_un_true_fijo()
        {
            Stamper(false).Stamp(ContactId);

            var update = Assert.Single(_crm.Updates);
            Assert.False(update.GetAttributeValue<bool>(SellableStamper.SellableAttribute));
        }

        [Fact]
        public void Sin_setting_no_escribe_nada()
        {
            // Sacar el App Setting es el interruptor para apagar el sellado: el contact
            // sincroniza solo si ya venia sellable, como antes de que existiera esto.
            var stamped = Stamper(null).Stamp(ContactId);

            Assert.False(stamped);
            Assert.Empty(_crm.Updates);
        }

        [Theory]
        [InlineData("true",  true)]
        [InlineData("True",  true)]
        [InlineData("false", false)]
        [InlineData(null,    null)]
        [InlineData("",      null)]
        [InlineData("si",    null)]   // no es false: false cortaria toda la sincronizacion
        [InlineData("1",     null)]
        public void El_setting_se_parsea_sin_caer_en_false_por_error(string? raw, bool? expected) =>
            Assert.Equal(expected, AppSettings.ParseSellableValue(raw));

        [Fact]
        public void El_update_solo_trae_el_campo_sellable()
        {
            // Cualquier otro atributo que se colara pisaria datos del contact.
            Stamper(true).Stamp(ContactId);

            var update = Assert.Single(_crm.Updates);
            Assert.Equal(
                new[] { SellableStamper.SellableAttribute },
                update.Attributes.Keys.ToArray());
        }
    }
}
