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
        ///   3. Niveles de log del WORKER (los de host.json no llegan aca).
        ///
        /// Uso en Program.cs:
        ///   var builder = FunctionsApplication.CreateBuilder(args);
        ///   builder.AddEipCore();
        ///   builder.Services.AddEipDataverse(builder.Configuration);
        ///   builder.Services.AddEipFoOData(builder.Configuration);   // si consume F&O
        ///
        /// Requiere "telemetryMode": "OpenTelemetry" en el host.json de la app: sin eso
        /// el host exporta por el SDK clasico de Application Insights mientras el worker
        /// exporta por OTel, y las trazas de los dos procesos no correlacionan.
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

            // Los filtros de host.json aplican SOLO a los logs del host. Lo que loguea
            // el codigo de las Functions pasa por aca, asi que las librerias ruidosas
            // se bajan en el worker o no se bajan en ningun lado.
            builder.Logging.AddFilter("Microsoft.PowerPlatform", LogLevel.Warning);
            builder.Logging.AddFilter("Azure.Messaging.ServiceBus", LogLevel.Warning);
            builder.Logging.AddFilter("Azure.Core", LogLevel.Warning);
            builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);

            // Consola solo fuera de Azure: el host captura stdout y lo reenvia al
            // pipeline de telemetria, asi que en Azure un ConsoleExporter duplica cada
            // log. En local es la unica forma de ver la salida del worker.
            if (string.IsNullOrEmpty(
                    Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")))
                builder.Services.AddLogging(b => b.AddConsole());

            return builder;
        }
    }
}
