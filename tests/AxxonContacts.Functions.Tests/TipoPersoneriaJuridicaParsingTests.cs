using AxxonContacts.Functions.Services;

namespace AxxonContacts.Functions.Tests
{
    /// <summary>
    /// axx_tipopersoneriajuridica es un OptionSet: en el RemoteExecutionContext no viaja
    /// como numero suelto sino envuelto en { "__type": "OptionSetValue:...", "Value": n }.
    /// Leerlo con el helper equivocado devuelve null en silencio y el master se crea sin
    /// el campo, sin error en ningun lado — la misma falla muda que ya costo el domicilio
    /// y el lugar comercial. Estos tests fijan la forma del payload.
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

        private static string OptionSet(string key, int value) => $$"""
            {
              "key": "{{key}}",
              "value": {
                "__type": "OptionSetValue:http://schemas.microsoft.com/xrm/2011/Contracts",
                "Value": {{value}}
              }
            }
            """;

        private const string ContactId = "dfe352a1-0948-f111-bec7-7c1e5268f183";
        private const string AccountId = "0f8cbb64-94e1-4d11-b22c-fca7e1faf35d";

        [Fact]
        public void El_contact_trae_el_valor_del_optionset()
        {
            var mensaje = ExecutionContextParser.Parse(
                Contexto(ContactId, "contact", OptionSet("axx_tipopersoneriajuridica", 727000003)));

            Assert.Equal(727000003, mensaje.AxxTipoPersoneriaJuridica);
        }

        [Fact]
        public void El_account_trae_el_valor_del_optionset()
        {
            var mensaje = AccountExecutionContextParser.Parse(
                Contexto(AccountId, "account", OptionSet("axx_tipopersoneriajuridica", 727000001)));

            Assert.Equal(727000001, mensaje.AxxTipoPersoneriaJuridica);
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
    }
}
