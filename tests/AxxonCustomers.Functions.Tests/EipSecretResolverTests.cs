using Axxon.Eip.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace AxxonCustomers.Functions.Tests
{
    /// <summary>
    /// Indireccion de nombres de secrets. Lo que esta en juego: los vaults legacy
    /// nombran los secrets por app registration y ambiente
    /// (INTE: "SecretNextGenDynamics365Inte"), no por el rol que cumplen. Sin la
    /// indireccion el nombre del ambiente terminaria hardcodeado en el core.
    /// </summary>
    public class EipSecretResolverTests
    {
        private static IConfiguration Config(params (string Key, string Value)[] entries) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(entries.Select(e =>
                    new KeyValuePair<string, string?>(e.Key, e.Value)))
                .Build();

        [Fact]
        public void Sin_indireccion_lee_la_clave_con_su_propio_nombre()
        {
            // El caso de test/DESA: el secret del vault se llama igual que la clave.
            var config = Config(("DataverseClientSecret", "valor-directo"));

            Assert.Equal("valor-directo", config.ResolveSecret("DataverseClientSecret"));
        }

        [Fact]
        public void Con_indireccion_lee_el_secret_que_nombra_el_app_setting()
        {
            // El caso de INTE: KeyVaultUri monta keyvaultinte y el provider publica
            // "SecretNextGenDynamics365Inte" como clave de configuracion.
            var config = Config(
                ("DataverseClientSecretName", "SecretNextGenDynamics365Inte"),
                ("SecretNextGenDynamics365Inte", "valor-del-vault"));

            Assert.Equal("valor-del-vault", config.ResolveSecret("DataverseClientSecret"));
        }

        [Fact]
        public void La_indireccion_pisa_el_valor_plano_que_haya_quedado()
        {
            // Durante el cutover conviven los dos: manda el del vault, no el app setting
            // plano que todavia no se borro.
            var config = Config(
                ("DataverseClientSecret", "valor-plano-viejo"),
                ("DataverseClientSecretName", "SecretNextGenDynamics365Inte"),
                ("SecretNextGenDynamics365Inte", "valor-del-vault"));

            Assert.Equal("valor-del-vault", config.ResolveSecret("DataverseClientSecret"));
        }

        [Fact]
        public void Un_nombre_que_no_resuelve_voltea_el_arranque()
        {
            // Sin esto la app cae en silencio a Managed Identity (UseClientSecretAuth
            // queda en false) y falla despues con un error que no menciona el secreto.
            var config = Config(("FoClientSecretName", "SecretQueNoExiste"));

            var ex = Assert.Throws<InvalidOperationException>(
                () => config.ResolveSecret("FoClientSecret"));

            Assert.Contains("FoClientSecretName", ex.Message);
            Assert.Contains("SecretQueNoExiste", ex.Message);
            Assert.Contains("Key Vault Secrets User", ex.Message);
        }

        [Fact]
        public void Sin_secreto_configurado_devuelve_null()
        {
            // Produccion con Managed Identity: no hay secreto que resolver y no es un error.
            Assert.Null(Config().ResolveSecret("DataverseClientSecret"));
        }
    }
}
