using System.Text.Json;
using Axxon.Eip.Core.Messaging;
using AxxonLeads.Functions.Models;

namespace AxxonLeads.Functions.Services
{
    /// <summary>
    /// Chequea que el mensaje cumpla el contrato ANTES de tocar Dataverse.
    ///
    /// Los campos obligatorios se validan aca y no se delegan a Dataverse a proposito: el
    /// error del SDK ante un campo requerido faltante es generico y no dice cual falta, y
    /// el satelite que mando el mensaje necesita esa respuesta en la razon del dead-letter
    /// para poder corregir. Ademas evita el viaje de red por un mensaje que ya sabemos que
    /// va a fallar.
    ///
    /// Devuelve el motivo del rechazo, o null si el mensaje esta completo — mismo contrato
    /// que <c>LtmCustSyncFunction.Validate</c>.
    /// </summary>
    public static class LeadEnvelopeValidator
    {
        public static string? Validate(EipMessage? envelope, out LeadIntakePayload? payload)
        {
            payload = null;

            if (envelope is null)
                return "El cuerpo del mensaje deserializo en null.";

            if (string.IsNullOrWhiteSpace(envelope.Source))
                return "El envelope no trae 'source'.";

            // Un envelope sin 'payload' deja el JsonElement en Undefined, y ahi GetPayload
            // no tira JsonException sino InvalidOperationException. Se chequea antes en
            // lugar de atrapar las dos: la que importa es la de contrato, y atrapar
            // InvalidOperationException a ciegas taparia bugs nuestros.
            if (envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return "El envelope no trae 'payload'.";

            try
            {
                payload = envelope.GetPayload<LeadIntakePayload>();
            }
            catch (JsonException ex)
            {
                return $"El payload no es un LeadIntakePayload valido: {ex.Message}";
            }

            if (payload is null)
                return "El envelope no trae 'payload'.";

            return ValidatePayload(payload);
        }

        /// <summary>
        /// Los obligatorios del lead. Publico para que los tests —y cualquier productor que
        /// quiera validar antes de encolar— usen exactamente la misma regla.
        /// </summary>
        public static string? ValidatePayload(LeadIntakePayload payload)
        {
            if (string.IsNullOrWhiteSpace(payload.Subject))
                return "El payload no trae 'subject' (Tema), que es obligatorio en el lead.";

            // Persona o empresa: Dataverse necesita al menos uno para que el lead tenga
            // nombre. Cual de los dos depende de si el satelite capto una persona o un
            // comercio, y no queremos que el contrato obligue a inventar el otro.
            if (string.IsNullOrWhiteSpace(payload.LastName) &&
                string.IsNullOrWhiteSpace(payload.CompanyName))
                return "El payload no trae ni 'lastName' ni 'companyName': el lead necesita al menos uno.";

            if (string.IsNullOrWhiteSpace(payload.IdentificationNumber))
                return "El payload no trae 'identificationNumber' (RUC o cedula), que es obligatorio.";

            return null;
        }
    }
}
