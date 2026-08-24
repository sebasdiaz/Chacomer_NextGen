using System.Net.Http.Headers;
using AxxonThinkchat.Functions.Configuration;

namespace AxxonThinkchat.Functions.Services
{
    /// <summary>
    /// Aplica la credencial de Thinkchat a un request. Vive aparte porque las dos
    /// operaciones que consumimos —get_templates y send_template— pegan al mismo
    /// endpoint con el mismo esquema de auth, y duplicarlo garantiza que un dia
    /// diverjan.
    /// </summary>
    internal static class ThinkchatAuth
    {
        public static void Apply(HttpRequestMessage request, ThinkchatOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                return;

            if (options.AuthHeader.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(options.AuthScheme))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(options.AuthScheme, options.ApiKey);
                return;
            }

            request.Headers.TryAddWithoutValidation(options.AuthHeader, options.ApiKey);
        }
    }
}
