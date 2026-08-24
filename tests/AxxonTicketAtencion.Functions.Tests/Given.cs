using AxxonTicketAtencion.Functions.Models;

namespace AxxonTicketAtencion.Functions.Tests
{
    /// <summary>
    /// Datos de ejemplo para los tests. Reemplaza al <c>TestGenerarWord.cs</c> que la
    /// implementacion original empaquetaba con la Function y escribia al Desktop.
    /// </summary>
    internal static class Given
    {
        /// <summary>Cita completa: todos los campos poblados, dos trabajos y dos notas.</summary>
        public static TicketAtencionData Ticket() => new()
        {
            NombreEmpresa  = "CHACOMER S.A.E.",
            NumeroCita     = "CA-2026-00123",
            FechaRecepcion = "24/08/2026 09:30",
            NombreTaller   = "Taller Central Asuncion",

            CodigoCliente = "CLI-000451",
            NombreCliente = "Maria Gonzalez",
            RazonSocial   = "Transportes del Este S.R.L.",
            Direccion     = "Avda. Mariscal Lopez 1234",
            Localidad     = "Asuncion",
            Telefono      = "+595 21 555 0100",

            Marca          = "Toyota",
            Modelo         = "Hilux SRV 4x4",
            Color          = "Blanco Perlado",
            NumeroMotor    = "2GD-1234567",
            NumeroChasis   = "8AJFR22G1N4512345",
            Patente        = "ABC 123",
            CodigoProducto = "HLX-SRV-24",
            KmRecorrido    = "48250",

            Descripcion    = "Ruido en tren delantero al pasar lomadas. Revisar amortiguadores.",
            AsesorServicio = "Juan Perez",
            TextoLegal     = "El cliente autoriza la revision del vehiculo en los terminos del contrato.",

            Trabajos = new[]
            {
                new TicketTrabajo("TRB-001", "Service de 50.000 km"),
                new TicketTrabajo("TRB-002", "Alineacion y balanceo")
            },

            NotasExternas = new[]
            {
                "El cliente retira el vehiculo despues de las 17:00.",
                "Dejar la rueda de auxilio en el baulera."
            }
        };

        /// <summary>Cita sin ningun dato: el peor caso para el binding del template.</summary>
        public static TicketAtencionData EmptyTicket() => new();
    }
}
