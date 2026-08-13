using Axxon.Eip.Core.Configuration;
using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Hosting;
using AxxonThinkchat.Functions.Configuration;
using AxxonThinkchat.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse (Managed Identity en Azure; client id/secret en DESA).
builder.Services.AddEipDataverse(builder.Configuration);

// El token vive en el Key Vault bajo el nombre "secretThinkChat". ResolveSecret cubre
// el caso de override via "ThinkchatApiKeyName"; si no esta seteado, se lee el secret
// por su nombre directo (lo publica el provider de Key Vault que monta AddEipCore).
const string ThinkchatSecretName = "secretThinkChat";

var thinkchat = new ThinkchatOptions
{
    BaseUrl       = builder.Configuration["ThinkchatBaseUrl"] ?? string.Empty,
    TemplatesPath = builder.Configuration["ThinkchatTemplatesPath"] ?? "get_template",
    From          = builder.Configuration["ThinkchatFrom"] ?? string.Empty,
    ApiKey        = builder.Configuration.ResolveSecret("ThinkchatApiKey")
                    ?? builder.Configuration[ThinkchatSecretName]
                    ?? string.Empty,
    AuthHeader    = builder.Configuration["ThinkchatAuthHeader"] ?? "Authorization",
    AuthScheme    = builder.Configuration["ThinkchatAuthScheme"] ?? "Bearer"
};

if (int.TryParse(builder.Configuration["ThinkchatTimeoutSeconds"], out var timeout) && timeout > 0)
    thinkchat.TimeoutSeconds = timeout;

builder.Services.AddSingleton(thinkchat);

builder.Services.AddHttpClient(ThinkchatTemplateService.HttpClientName, client =>
{
    if (!string.IsNullOrWhiteSpace(thinkchat.BaseUrl))
    {
        // La BaseAddress necesita barra final para que el path relativo no pise el ultimo segmento.
        var baseUrl = thinkchat.BaseUrl.EndsWith('/') ? thinkchat.BaseUrl : thinkchat.BaseUrl + "/";
        client.BaseAddress = new Uri(baseUrl);
    }

    client.Timeout = TimeSpan.FromSeconds(thinkchat.TimeoutSeconds);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddTransient<IThinkchatTemplateService>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(ThinkchatTemplateService.HttpClientName);

    return new ThinkchatTemplateService(
        httpClient,
        sp.GetRequiredService<ThinkchatOptions>(),
        sp.GetRequiredService<ILogger<ThinkchatTemplateService>>());
});

builder.Services.AddTransient<ITemplateSyncService>(sp =>
{
    var factory    = sp.GetRequiredService<DataverseClientFactory>();
    var orgService = factory.CreateOrganizationService();
    var logger     = sp.GetRequiredService<ILogger<TemplateSyncService>>();
    return new TemplateSyncService(orgService, logger);
});

builder.Build().Run();
