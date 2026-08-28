using System.Text.Json;
using Axxon.Eip.Core.Dataverse;
using AxxonTicketAtencion.Functions.Models;
using Microsoft.Extensions.Logging;

namespace AxxonTicketAtencion.Functions.Services
{
    /// <summary>Resuelve contra Dataverse todos los datos de una Cita de Servicio.</summary>
    public interface ITicketAtencionDataService
    {
        /// <summary>
        /// Devuelve los datos del ticket, o null si la Cita no existe.
        /// Cualquier otra falla de Dataverse lanza: un ticket incompleto es peor que un error.
        /// </summary>
        Task<TicketAtencionData?> GetAsync(Guid serviceAppointmentId, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="ITicketAtencionDataService"/>
    public sealed class TicketAtencionDataService : ITicketAtencionDataService
    {
        private readonly IDataverseWebApiClient _dataverse;
        private readonly ILogger<TicketAtencionDataService> _logger;

        public TicketAtencionDataService(
            IDataverseWebApiClient dataverse,
            ILogger<TicketAtencionDataService> logger)
        {
            _dataverse = dataverse;
            _logger    = logger;
        }

        public async Task<TicketAtencionData?> GetAsync(
            Guid serviceAppointmentId, CancellationToken cancellationToken = default)
        {
            var appointment = await _dataverse.GetRecordAsync(
                BuildAppointmentQuery(serviceAppointmentId), "Cita", cancellationToken);

            if (appointment is null)
                return null;

            var sa         = appointment.Value;
            var deviceId   = GetString(sa, "_msauto_deviceid_value");
            var customerId = GetString(sa, "_msauto_customerid_value");
            var companyId  = GetString(sa, "_a365_company_value");

            // Las cinco secundarias no dependen entre si: van juntas. Con una instancia
            // en frio, en serie son cinco round-trips encadenados contra Dataverse.
            var jobsTask    = GetJobsAsync(serviceAppointmentId, cancellationToken);
            var notesTask   = GetNotesAsync(serviceAppointmentId, cancellationToken);
            var kmTask      = GetLastMeasurementAsync(deviceId, cancellationToken);
            var addressTask = GetAddressAsync(customerId, cancellationToken);
            var companyTask = GetCompanyAsync(companyId, cancellationToken);

            await Task.WhenAll(jobsTask, notesTask, kmTask, addressTask, companyTask);

            var (direccion, localidad) = addressTask.Result;
            var (empresa, textoLegal)  = companyTask.Result;

            var contact = GetObject(sa, "msauto_CustomerId_contact");
            var account = GetObject(sa, "msauto_CustomerId_account");
            var device  = GetObject(sa, "msauto_DeviceId");

            return new TicketAtencionData
            {
                NombreEmpresa  = empresa,
                NumeroCita     = GetString(sa, "a365_identifier"),
                FechaRecepcion = ParaguayTime.FormatUtc(GetString(sa, "axx_receptiondatetime")),
                NombreTaller   = GetString(sa, "msauto_BusinessOperationId", "msauto_name"),

                // El codigo sale del contacto cuando el cliente es persona fisica, y de la
                // cuenta cuando es juridica. Nunca hay ambos.
                CodigoCliente = contact is not null
                    ? GetString(contact.Value, "msdyn_contactpersonid")
                    : account is not null ? GetString(account.Value, "accountnumber") : string.Empty,
                NombreCliente = contact is not null
                    ? $"{GetString(contact.Value, "firstname")} {GetString(contact.Value, "lastname")}".Trim()
                    : string.Empty,
                RazonSocial = account is not null ? GetString(account.Value, "name") : string.Empty,
                Direccion   = direccion,
                Localidad   = localidad,
                Telefono    = GetString(sa, "axx_serviceappointmentphone"),

                Marca          = GetString(device, "msauto_DeviceBrandId", "msauto_name"),
                Modelo         = GetString(device, "msauto_DeviceModelId", "msauto_name"),
                Color          = GetString(device, "a365_deviceexteriorid", "a365_name"),
                NumeroMotor    = GetString(device, "axx_numeromotor"),
                NumeroChasis   = GetString(device, "msauto_chassisnumber"),
                Patente        = GetString(device, "msauto_registrationnumber"),
                CodigoProducto = GetString(device, "msauto_DeviceModelCodeId", "msauto_description"),
                KmRecorrido    = kmTask.Result,

                Descripcion    = GetString(sa, "msauto_description"),
                AsesorServicio = GetString(sa, "a365_arrivalserviceadvisorid", "a365_name"),
                TextoLegal     = textoLegal,

                Trabajos      = jobsTask.Result,
                NotasExternas = notesTask.Result
            };
        }

        // -- Queries -------------------------------------------------------

        /// <summary>
        /// Query principal: la Cita con todo lo que se puede traer por $expand.
        ///
        /// _msauto_customerid_value tiene que estar en el $select aunque no se muestre en el
        /// documento: Dataverse no devuelve lookups que no se pidieron, y sin el no hay con
        /// que filtrar customeraddresses -> Direccion y Localidad salen vacias. Es el valor
        /// de un lookup Customer, asi que trae el id del contact o el del account segun el
        /// caso; no existe una columna _..._account_value para pedir solo uno de los dos.
        ///
        /// Los nombres de aca son los de la version de Annata instalada, y no los que uno
        /// esperaria del modelo generico: el asesor es a365_arrivalserviceadvisorid (no
        /// msauto_ServiceAdvisorId) y el color a365_deviceexteriorid (no a365_ExteriorColorId).
        /// Los tests mockean Dataverse, asi que un nombre inexistente no se cae en el build:
        /// sale como 400 BadRequest recien contra el ambiente.
        /// </summary>
        private static string BuildAppointmentQuery(Guid id)
        {
            const string select =
                "$select=a365_identifier,axx_receptiondatetime,axx_serviceappointmentphone," +
                "msauto_description,_msauto_deviceid_value,_a365_company_value," +
                "_msauto_customerid_value";

            const string expand =
                "$expand=msauto_BusinessOperationId($select=msauto_name)," +
                // El de recepcion, no el de entrega (a365_deliveryserviceadvisorid): el
                // ticket es la orden que se firma al dejar el vehiculo.
                "a365_arrivalserviceadvisorid($select=a365_name)," +
                "msauto_DeviceId($select=msauto_chassisnumber,msauto_registrationnumber,axx_numeromotor;" +
                    "$expand=msauto_DeviceBrandId($select=msauto_name)," +
                            "msauto_DeviceModelId($select=msauto_name)," +
                            "msauto_DeviceModelCodeId($select=msauto_description)," +
                            "a365_deviceexteriorid($select=a365_name))," +
                "msauto_CustomerId_contact($select=msdyn_contactpersonid,firstname,lastname)," +
                "msauto_CustomerId_account($select=accountnumber,name)";

            return $"msauto_serviceappointments({id})?{select}&{expand}";
        }

        private async Task<IReadOnlyList<TicketTrabajo>> GetJobsAsync(
            Guid serviceAppointmentId, CancellationToken cancellationToken)
        {
            var rows = await _dataverse.GetArrayAsync(
                $"msauto_serviceorderjobs?$filter=_a365_serviceappointmentid_value eq {serviceAppointmentId}" +
                "&$select=a365_identifier,msauto_description&$orderby=createdon asc",
                "Trabajos", cancellationToken);

            return rows
                .Select(r => new TicketTrabajo(
                    GetString(r, "a365_identifier"),
                    GetString(r, "msauto_description")))
                .ToList();
        }

        private async Task<IReadOnlyList<string>> GetNotesAsync(
            Guid serviceAppointmentId, CancellationToken cancellationToken)
        {
            var rows = await _dataverse.GetArrayAsync(
                $"a365_externalnotes?$filter=_a365_serviceappointmentid_value eq {serviceAppointmentId}" +
                "&$select=a365_note&$orderby=createdon asc",
                "Notas", cancellationToken);

            return rows.Select(r => GetString(r, "a365_note")).ToList();
        }

        private async Task<string> GetLastMeasurementAsync(string deviceId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(deviceId, out var id))
                return string.Empty;

            var rows = await _dataverse.GetArrayAsync(
                $"msauto_devicemeasurements?$filter=_msauto_deviceid_value eq {id}" +
                "&$select=msauto_value&$orderby=createdon desc&$top=1",
                "Kilometraje", cancellationToken);

            return rows.Count > 0 ? GetString(rows[0], "msauto_value") : string.Empty;
        }

        /// <summary>
        /// Direccion y localidad del cliente de la Cita.
        ///
        /// El id que llega es el del lookup Customer: puede ser un contact o un account. No
        /// hace falta ramificar porque customeraddress.parentid tambien es polimorfico y
        /// apunta a las dos, asi que el mismo filtro sirve para persona fisica y juridica.
        /// </summary>
        private async Task<(string Direccion, string Localidad)> GetAddressAsync(
            string customerId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(customerId, out var id))
            {
                _logger.LogInformation(
                    "[TicketAtencion] La Cita no tiene cliente asociado: el ticket sale sin direccion.");
                return (string.Empty, string.Empty);
            }

            var rows = await _dataverse.GetArrayAsync(
                $"customeraddresses?$filter=_parentid_value eq {id}" +
                "&$select=line1,city&$orderby=createdon asc&$top=1",
                "Direccion", cancellationToken);

            return rows.Count > 0
                ? (GetString(rows[0], "line1"), GetString(rows[0], "city"))
                : (string.Empty, string.Empty);
        }

        private async Task<(string Nombre, string TextoLegal)> GetCompanyAsync(
            string companyId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(companyId, out var id))
                return (string.Empty, string.Empty);

            var company = await _dataverse.GetRecordAsync(
                $"cdm_companies({id})?$select=cdm_name,axx_legaltext", "Empresa", cancellationToken);

            return company is null
                ? (string.Empty, string.Empty)
                : (GetString(company.Value, "cdm_name"), GetString(company.Value, "axx_legaltext"));
        }

        // -- Lectura de JSON -----------------------------------------------

        private static JsonElement? GetObject(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
                ? value
                : null;

        private static string GetString(JsonElement? element, string property, string? nested = null) =>
            element is null ? string.Empty : GetString(element.Value, property, nested);

        private static string GetString(JsonElement element, string property, string? nested = null)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return string.Empty;

            if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                return string.Empty;

            if (nested is not null)
                return GetString(value, nested);

            // Los numericos (msauto_value) y los booleanos llegan sin comillas: GetString()
            // devolveria null para ellos, asi que se cae a la representacion cruda.
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }
    }
}
