using AxxonContacts.Functions.Services;

namespace AxxonContacts.Functions.Tests
{
    /// <summary>
    /// axx_tipopersoneriajuridica es un <b>Lookup</b> a la tabla axx_personeriajuridia — no un
    /// OptionSet, como se asumio hasta el 2026-09-04. En el RemoteExecutionContext viaja
    /// envuelto en { "__type": "EntityReference:...", "Id": "guid", "LogicalName": "..." }.
    ///
    /// Leerlo con el helper equivocado no falla al compilar ni al parsear: devuelve null en
    /// silencio y el master se crea sin el campo. Peor todavia del lado de Dataverse, donde
    /// castearlo a OptionSetValue lanza "Unable to cast EntityReference to OptionSetValue"
    /// dentro de un catch que lo degrada a warning. Es la misma falla muda que ya costo el
    /// domicilio y el lugar comercial. Estos tests fijan la forma del payload.
    /// </summary>
    public class TipoPersoneriaJuridicaParsingTests
    {
        private static string Contexto(string entityId, string idKey, string attributes) => $$"""
            {
              "MessageName": "Update",
              "PrimaryEntityId": "{{entityId}}",
              "PrimaryEntityName": "{{idKey}}",
              "InputParameters": [
                {
                  "key": "Target",
                  "value": {
                    "__type": "Entity:http://schemas.microsoft.com/xrm/2011/Contracts",
                    "Attributes": [ {{attributes}} ]
                  }
                }
              ]
            }
            """;

        private static string Lookup(string key, string id, string logicalName) => $$"""
            {
              "key": "{{key}}",
              "value": {
                "__type": "EntityReference:http://schemas.microsoft.com/xrm/2011/Contracts",
                "Id": "{{id}}",
                "LogicalName": "{{logicalName}}"
              }
            }
            """;

        private static string OptionSet(string key, int value) => $$"""
            {
              "key": "{{key}}",
              "value": {
                "__type": "OptionSetValue:http://schemas.microsoft.com/xrm/2011/Contracts",
                "Value": {{value}}
              }
            }
            """;

        private const string ContactId    = "dfe352a1-0948-f111-bec7-7c1e5268f183";
        private const string AccountId    = "0f8cbb64-94e1-4d11-b22c-fca7e1faf35d";
        private const string PersoneriaId = "4e84812a-116c-f111-a826-3833c5dd7965";
        private const string PersoneriaLn = "axx_personeriajuridia";

        [Fact]
        public void El_contact_trae_el_id_del_lookup()
        {
            var mensaje = ExecutionContextParser.Parse(
                Contexto(ContactId, "contact",
                    Lookup("axx_tipopersoneriajuridica", PersoneriaId, PersoneriaLn)));

            Assert.Equal(Guid.Parse(PersoneriaId), mensaje.AxxTipoPersoneriaJuridica);
        }

        [Fact]
        public void El_account_trae_el_id_del_lookup()
        {
            var mensaje = AccountExecutionContextParser.Parse(
                Contexto(AccountId, "account",
                    Lookup("axx_tipopersoneriajuridica", PersoneriaId, PersoneriaLn)));

            Assert.Equal(Guid.Parse(PersoneriaId), mensaje.AxxTipoPersoneriaJuridica);
        }

        [Fact]
        public void Sin_el_atributo_queda_null_y_el_enrich_lo_completa_desde_Dataverse()
        {
            var contact = ExecutionContextParser.Parse(
                Contexto(ContactId, "contact", OptionSet("statuscode", 1)));
            var account = AccountExecutionContextParser.Parse(
                Contexto(AccountId, "account", OptionSet("statuscode", 1)));

            Assert.Null(contact.AxxTipoPersoneriaJuridica);
            Assert.Null(account.AxxTipoPersoneriaJuridica);
        }

        /// <summary>
        /// Guarda contra la regresion: si alguien vuelve a mandar el campo como OptionSet
        /// —o el environment lo cambia de tipo— el parser tiene que dar null y no un Guid
        /// inventado, para que el enrich lo complete desde Dataverse en vez de escribir mal.
        /// </summary>
        [Fact]
        public void Un_payload_con_forma_de_optionset_no_produce_id()
        {
            var contact = ExecutionContextParser.Parse(
                Contexto(ContactId, "contact", OptionSet("axx_tipopersoneriajuridica", 727000003)));
            var account = AccountExecutionContextParser.Parse(
                Contexto(AccountId, "account", OptionSet("axx_tipopersoneriajuridica", 727000001)));

            Assert.Null(contact.AxxTipoPersoneriaJuridica);
            Assert.Null(account.AxxTipoPersoneriaJuridica);
        }
    }
}
