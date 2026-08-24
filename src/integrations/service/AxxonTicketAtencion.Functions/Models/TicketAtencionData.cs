namespace AxxonTicketAtencion.Functions.Models
{
    /// <summary>Una linea de trabajo solicitado (Service Order Job) de la Cita.</summary>
    public sealed record TicketTrabajo(string Codigo, string Descripcion);

    /// <summary>
    /// Datos ya resueltos de una Cita de Servicio, listos para volcarse al XML del template.
    ///
    /// Es el limite entre "hablar con Dataverse" (<c>TicketAtencionDataService</c>) y "armar
    /// el documento" (<c>TicketXmlBuilder</c>): todo lo que llega aca es string formateado,
    /// asi que el armado del XML se testea sin tocar la red.
    ///
    /// Los campos son string y no null: un dato ausente es cadena vacia. El binding del
    /// template espera todos los elementos presentes, aunque vengan vacios.
    /// </summary>
    public sealed record TicketAtencionData
    {
        public string NombreEmpresa  { get; init; } = string.Empty;
        public string NumeroCita     { get; init; } = string.Empty;
        public string FechaRecepcion { get; init; } = string.Empty;
        public string NombreTaller   { get; init; } = string.Empty;

        public string CodigoCliente { get; init; } = string.Empty;
        public string NombreCliente { get; init; } = string.Empty;
        public string RazonSocial   { get; init; } = string.Empty;
        public string Direccion     { get; init; } = string.Empty;
        public string Localidad     { get; init; } = string.Empty;
        public string Telefono      { get; init; } = string.Empty;

        public string Marca          { get; init; } = string.Empty;
        public string Modelo         { get; init; } = string.Empty;
        public string Color          { get; init; } = string.Empty;
        public string NumeroMotor    { get; init; } = string.Empty;
        public string NumeroChasis   { get; init; } = string.Empty;
        public string Patente        { get; init; } = string.Empty;
        public string CodigoProducto { get; init; } = string.Empty;
        public string KmRecorrido    { get; init; } = string.Empty;

        public string Descripcion    { get; init; } = string.Empty;
        public string AsesorServicio { get; init; } = string.Empty;
        public string TextoLegal     { get; init; } = string.Empty;

        public IReadOnlyList<TicketTrabajo> Trabajos { get; init; } = Array.Empty<TicketTrabajo>();

        public IReadOnlyList<string> NotasExternas { get; init; } = Array.Empty<string>();
    }
}
