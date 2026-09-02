using System.Text.Json;
using Axxon.Eip.Core.Messaging;
using AxxonLeads.Functions.Configuration;
using AxxonLeads.Functions.Models;

namespace AxxonLeads.Functions.Tests
{
    /// <summary>
    /// Armado de mensajes y payloads para los tests. La base es valida: cada test rompe
    /// o extiende solo lo que esta probando.
    /// </summary>
    internal static class Given
    {
        public const string ExternalIdAttribute = "axx_externalid";

        /// <summary>Payload minimo que pasa la validacion: los tres obligatorios.</summary>
        public static LeadIntakePayload Payload() => new()
        {
            Subject              = "Consulta por camion",
            LastName             = "Diaz",
            IdentificationNumber = "80012345-6"
        };

        /// <summary>Domicilio completo, para los tests de mapeo de direccion.</summary>
        public static LeadAddress Address() => new()
        {
            Name            = "Casa central",
            Line1           = "Avda. Mariscal Lopez",
            Line2           = "1234",
            Line3           = "Piso 3",
            City            = "Asuncion",
            StateOrProvince = "Central",
            PostalCode      = "1209",
            Country         = "Paraguay",
            Telephone       = "021 555 000"
        };

        /// <summary>Opciones con la deduplicacion apagada (el default de un org sin columna propia).</summary>
        public static LeadIntakeOptions Options() => new();

        /// <summary>Opciones con la deduplicacion prendida.</summary>
        public static LeadIntakeOptions OptionsWithDedup() => new()
        {
            ExternalIdAttribute = ExternalIdAttribute
        };

        /// <summary>
        /// Envelope EiP con el payload adentro, serializado y vuelto a leer como
        /// <see cref="EipMessage"/> — igual que lo recibe la Function.
        /// </summary>
        public static EipMessage Envelope(
            LeadIntakePayload payload,
            string source = "thinkchat")
        {
            var typed = EipMessage<LeadIntakePayload>.Create(
                source:     source,
                entityType: "lead",
                operation:  EipOperation.Create,
                payload:    payload);

            var json = JsonSerializer.Serialize(typed, EipMessageDefaults.SerializerOptions);

            return JsonSerializer.Deserialize<EipMessage>(json, EipMessageDefaults.SerializerOptions)!;
        }
    }
}
