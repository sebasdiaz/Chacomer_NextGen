using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Axxon.Eip.Core.Configuration
{
    /// <summary>
    /// Integra Azure Key Vault como fuente de configuracion del worker.
    ///
    /// Como funciona:
    ///   - Si el app setting "KeyVaultUri" esta presente, se agrega el vault como
    ///     configuration provider. Cada secret del vault se expone como clave de
    ///     configuracion con su mismo nombre (ej: secret "DataverseClientSecret"
    ///     → configuration["DataverseClientSecret"]).
    ///   - El provider se agrega DESPUES de las variables de entorno, por lo que
    ///     un secret en Key Vault pisa cualquier valor duplicado en App Settings.
    ///   - Autentica con DefaultAzureCredential: Managed Identity en Azure
    ///     (requiere rol "Key Vault Secrets User" sobre el vault) y az login /
    ///     Visual Studio en desarrollo local.
    ///   - Sin "KeyVaultUri" configurado no hace nada: en local se puede seguir
    ///     trabajando con local.settings.json.
    ///
    /// IMPORTANTE: este provider alimenta la configuracion del WORKER (codigo).
    /// Los settings que resuelve el HOST de Functions (connection de triggers como
    /// "ServiceBusConnection", binding expressions %...%) NO pasan por aca: para
    /// esos usar Key Vault references en los Application Settings de la Function App
    /// (@Microsoft.KeyVault(SecretUri=...)) o Managed Identity sin secreto.
    /// </summary>
    public static class EipKeyVaultExtensions
    {
        public const string KeyVaultUriKey = "KeyVaultUri";

        public static IConfigurationManager AddEipKeyVault(this IConfigurationManager configuration)
        {
            var vaultUri = configuration[KeyVaultUriKey];

            if (string.IsNullOrWhiteSpace(vaultUri))
                return configuration;

            configuration.AddAzureKeyVault(
                new Uri(vaultUri),
                new DefaultAzureCredential());

            return configuration;
        }
    }
}
