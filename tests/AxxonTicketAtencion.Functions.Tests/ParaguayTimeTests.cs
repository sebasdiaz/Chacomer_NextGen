using AxxonTicketAtencion.Functions.Services;

namespace AxxonTicketAtencion.Functions.Tests
{
    /// <summary>
    /// La fecha de recepcion sale de Dataverse en UTC. Con <c>ToLocalTime()</c> —lo que
    /// hacia la implementacion original— una Function App en Azure la deja en UTC y el
    /// ticket muestra 3 horas de mas.
    /// </summary>
    public class ParaguayTimeTests
    {
        [Theory]
        // Paraguay quedo en UTC-3 permanente tras eliminar el horario de verano en 2024,
        // asi que el desfase es el mismo en invierno que en verano.
        [InlineData("2026-08-24T12:30:00Z", "24/08/2026 09:30")]  // invierno
        [InlineData("2026-01-15T02:00:00Z", "14/01/2026 23:00")]  // verano, cruza el dia hacia atras
        [InlineData("2026-12-31T23:59:00Z", "31/12/2026 20:59")]
        public void Convierte_de_utc_a_hora_de_paraguay(string utc, string esperado)
        {
            Assert.Equal(esperado, ParaguayTime.FormatUtc(utc));
        }

        [Fact]
        public void No_depende_de_la_base_de_zonas_del_sistema()
        {
            // Hay hosts cuya tzdata todavia trae el horario de verano derogado y devuelven
            // UTC-4 en invierno. El ticket no puede imprimir distinto segun donde corra.
            Assert.Equal(TimeSpan.FromHours(-3), ParaguayTime.Offset);

            var invierno = ParaguayTime.FormatUtc("2026-08-24T12:30:00Z");
            var verano   = ParaguayTime.FormatUtc("2026-02-24T12:30:00Z");

            Assert.Equal("09:30", invierno[^5..]);
            Assert.Equal("09:30", verano[^5..]);
        }

        [Fact]
        public void Interpreta_como_utc_una_fecha_sin_offset()
        {
            // Dataverse manda siempre con Z, pero un valor sin offset no puede tomarse como
            // hora local del server (que en Azure es UTC igual, pero no es algo que valga
            // la pena dejar librado al host).
            Assert.Equal("24/08/2026 09:30", ParaguayTime.FormatUtc("2026-08-24T12:30:00"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Un_valor_vacio_da_cadena_vacia(string? valor)
        {
            Assert.Equal(string.Empty, ParaguayTime.FormatUtc(valor));
        }

        [Fact]
        public void Un_valor_no_parseable_se_devuelve_tal_cual()
        {
            // El documento sale con lo que haya en lugar de fallar por una fecha rara.
            Assert.Equal("no es una fecha", ParaguayTime.FormatUtc("no es una fecha"));
        }
    }
}
