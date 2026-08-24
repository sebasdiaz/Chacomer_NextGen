using System.Globalization;

namespace AxxonTicketAtencion.Functions.Services
{
    /// <summary>
    /// Formatea las fechas del ticket en hora de Paraguay.
    ///
    /// Por que no <c>ToLocalTime()</c>: la hora local de una Function App en Azure es UTC,
    /// asi que el ticket sale con horas de mas.
    ///
    /// Por que tampoco <c>TimeZoneInfo.FindSystemTimeZoneById</c>: Paraguay elimino el
    /// horario de verano a partir de octubre de 2024 y quedo en UTC-3 permanente, pero hay
    /// maquinas y contenedores cuya base de zonas todavia trae las reglas viejas (UTC-4 con
    /// verano UTC-3). En una de esas, un ticket de agosto sale con una hora menos — y como
    /// depende del host, el mismo codigo imprime distinto segun donde corra. Verificado:
    /// sobre este entorno de desarrollo, "America/Asuncion" devuelve UTC-4 en agosto de 2026.
    ///
    /// El offset fijo es correcto para toda fecha posterior a la reforma, que es el universo
    /// de una cita de taller. SI PARAGUAY VUELVE A APLICAR HORARIO DE VERANO, esto hay que
    /// cambiarlo aca — no se va a arreglar solo con una actualizacion del sistema operativo.
    /// </summary>
    public static class ParaguayTime
    {
        /// <summary>UTC-3 permanente desde la reforma de octubre de 2024.</summary>
        public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

        /// <summary>Formato que espera el template.</summary>
        public const string Format = "dd/MM/yyyy HH:mm";

        /// <summary>
        /// Convierte un instante ISO 8601 de Dataverse (siempre UTC) al formato del ticket.
        /// Un valor vacio o no parseable se devuelve tal cual: el documento sale con lo que
        /// haya en lugar de fallar por una fecha.
        /// </summary>
        public static string FormatUtc(string? isoUtc)
        {
            if (string.IsNullOrWhiteSpace(isoUtc))
                return string.Empty;

            // AssumeUniversal: un valor sin offset se toma como UTC (que es lo que manda
            // Dataverse) y no como hora local del host.
            if (!DateTimeOffset.TryParse(isoUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var utc))
                return isoUtc;

            return utc.ToOffset(Offset).ToString(Format, CultureInfo.InvariantCulture);
        }
    }
}
