using Microsoft.Extensions.Configuration;

namespace Axxon.Eip.Core.Configuration
{
    /// <summary>
    /// Resuelve un secreto permitiendo que el secret del Key Vault se llame distinto
    /// que la clave de configuracion que lo consume.
    ///
    /// Por que hace falta: los vaults legacy nombran los secrets por app registration
    /// y ambiente (ej: "SecretNextGenDynamics365Inte"), no por el rol que cumplen
    /// ("DataverseClientSecret"). El nombre lleva el ambiente adentro, asi que no puede
    /// quedar hardcodeado en el codigo: se resuelve por indireccion, igual que "KeyVaultUri".
    ///
    /// Como funciona, para una clave "X":
    ///   - Si el app setting "XName" esta seteado, se devuelve configuration[su valor].
    ///     Esa clave la publica el provider de Key Vault que monta AddEipCore().
    ///   - Si no, se devuelve configuration["X"] — el comportamiento de siempre.
    ///
    /// Ejemplo (app settings de fa-axxoncustomers-inte):
    ///   KeyVaultUri              = https://keyvaultinte.vault.azure.net/
    ///   DataverseClientSecretName = SecretNextGenDynamics365Inte
    ///   → configuration.ResolveSecret("DataverseClientSecret") devuelve el valor del
    ///     secret "SecretNextGenDynamics365Inte" del vault.
    /// </summary>
    public static class EipSecretResolver
    {
        /// <summary>Sufijo del app setting que indica el nombre real del secret.</summary>
        public const string NameSuffix = "Name";

        /// <summary>
        /// Devuelve el valor del secreto de <paramref name="key"/>, siguiendo la
        /// indireccion de "{key}Name" si esta configurada.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Si "{key}Name" apunta a una clave que no resuelve. Es intencionalmente fatal:
        /// devolver null dejaria a DataverseOptions.UseClientSecretAuth en false y la app
        /// caeria en silencio a Managed Identity, fallando mas adelante con un error que
        /// no menciona el secreto mal configurado.
        /// </exception>
        public static string? ResolveSecret(this IConfiguration configuration, string key)
        {
            var secretName = configuration[key + NameSuffix];

            if (string.IsNullOrWhiteSpace(secretName))
                return configuration[key];

            var value = configuration[secretName];

            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    $"'{key}{NameSuffix}' apunta al secret '{secretName}', que no se pudo resolver. " +
                    $"Verificar que el secret exista en el Key Vault de '{EipKeyVaultExtensions.KeyVaultUriKey}' " +
                    "y que la Managed Identity de la Function App tenga el rol 'Key Vault Secrets User' sobre el vault.");

            return value;
        }
    }
}
