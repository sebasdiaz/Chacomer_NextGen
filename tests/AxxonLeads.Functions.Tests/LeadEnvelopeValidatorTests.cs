using System.Text.Json;
using Axxon.Eip.Core.Messaging;
using AxxonLeads.Functions.Services;

namespace AxxonLeads.Functions.Tests
{
    /// <summary>
    /// El contrato del mensaje. Lo que estos tests fijan es que un mensaje incompleto se
    /// rechace ACA y no en Dataverse: el satelite necesita saber que campo le falta, y eso
    /// solo puede decirselo la razon del dead-letter.
    /// </summary>
    public class LeadEnvelopeValidatorTests
    {
        [Fact]
        public void Un_mensaje_completo_pasa()
        {
            var error = LeadEnvelopeValidator.Validate(Given.Envelope(Given.Payload()), out var payload);

            Assert.Null(error);
            Assert.NotNull(payload);
            Assert.Equal("80012345-6", payload!.IdentificationNumber);
        }

        [Fact]
        public void Sin_subject_se_rechaza()
        {
            var payload = Given.Payload();
            payload.Subject = "   ";

            var error = LeadEnvelopeValidator.Validate(Given.Envelope(payload), out _);

            Assert.Contains("subject", error);
        }

        [Fact]
        public void Sin_identificationNumber_se_rechaza()
        {
            var payload = Given.Payload();
            payload.IdentificationNumber = null;

            var error = LeadEnvelopeValidator.Validate(Given.Envelope(payload), out _);

            Assert.Contains("identificationNumber", error);
        }

        [Fact]
        public void Sin_lastName_ni_companyName_se_rechaza()
        {
            var payload = Given.Payload();
            payload.LastName = null;

            var error = LeadEnvelopeValidator.Validate(Given.Envelope(payload), out _);

            Assert.Contains("companyName", error);
        }

        [Fact]
        public void Con_companyName_y_sin_lastName_pasa()
        {
            // El lead de un comercio: no hay persona, pero el lead igual tiene nombre.
            var payload = Given.Payload();
            payload.LastName    = null;
            payload.CompanyName = "Ferreteria San Blas S.A.";

            var error = LeadEnvelopeValidator.Validate(Given.Envelope(payload), out _);

            Assert.Null(error);
        }

        [Fact]
        public void Sin_source_se_rechaza()
        {
            var envelope = Given.Envelope(Given.Payload(), source: "");

            var error = LeadEnvelopeValidator.Validate(envelope, out _);

            Assert.Contains("source", error);
        }

        [Fact]
        public void Un_envelope_sin_payload_se_rechaza()
        {
            var envelope = JsonSerializer.Deserialize<EipMessage>(
                """{"source":"thinkchat","entityType":"lead","operation":"create"}""",
                EipMessageDefaults.SerializerOptions);

            var error = LeadEnvelopeValidator.Validate(envelope, out var payload);

            Assert.NotNull(error);
            Assert.Null(payload);
        }

        [Fact]
        public void Un_payload_con_el_tipo_equivocado_se_rechaza_sin_explotar()
        {
            // leadSourceCode como texto: el satelite mando un optionset mal tipado.
            // Tiene que salir como violacion de contrato, no como una excepcion sin atrapar.
            var envelope = JsonSerializer.Deserialize<EipMessage>(
                """
                {"source":"thinkchat","entityType":"lead","operation":"create",
                 "payload":{"subject":"x","lastName":"y","identificationNumber":"z",
                            "leadSourceCode":"web"}}
                """,
                EipMessageDefaults.SerializerOptions);

            var error = LeadEnvelopeValidator.Validate(envelope, out _);

            Assert.Contains("LeadIntakePayload", error);
        }
    }
}
