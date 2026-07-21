using Axxon.Eip.Core.Configuration;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Azure.Monitor.OpenTelemetry.Exporter;

namespace Axxon.Eip.Core.Hosting
{
    /// <summary>
    /// Bootstrap comun de todas las Function Apps de la EiP.
    /// </summary>
    public static class EipHostingExtensions
    {
        /// <summary>
        /// Configura lo cross a toda la plataforma:
        ///   1. Key Vault como fuente de secretos (si "KeyVaultUri" esta configurado).
        ///   2. OpenTelemetry + exporter a Application Insights
        ///      (si "APPLICATIONINSIGHTS_CONNECTION_STRING" esta configurado).
        ///   3. Logging a consola.
        ///
        /// Uso en Program.cs:
        ///   var builder = FunctionsApplication.CreateBuilder(args);
        ///   builder.AddEipCore();
        ///   builder.Services.AddEipDataverse(builder.Configuration);
        ///   builder.Services.AddEipFoOData(builder.Configuration);   // si consume F&O
        /// </summary>
        public static IHostApplicationBuilder AddEipCore(this IHostApplicationBuilder builder)
        {
            // Key Vault primero: los secrets del vault pisan App Settings duplicados
            // y quedan disponibles para el resto de los registros.
            builder.Configuration.AddEipKeyVault();

            var otelBuilder = builder.Services.AddOpenTelemetry()
                .UseFunctionsWorkerDefaults();

            var appInsightsConnectionString =
                builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
                otelBuilder.UseAzureMonitorExporter();

            builder.Services.AddLogging(b => b.AddConsole());

            return builder;
        }
    }
}
