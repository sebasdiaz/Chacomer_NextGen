using Axxon.Eip.Core.Dataverse;
using Axxon.Eip.Core.Graph;
using Axxon.Eip.Core.Hosting;
using AxxonTicketAtencion.Functions.Documents;
using AxxonTicketAtencion.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

// Cross EiP: Key Vault, OpenTelemetry/App Insights, logging.
builder.AddEipCore();

// Dataverse por Web API (OData) y no por el SDK: la consulta principal de la Cita tiene
// $expand anidados de dos niveles (Cita -> Dispositivo -> Marca/Modelo/Color) que en
// FetchXML quedan mucho mas oscuros.
builder.Services.AddEipDataverseWebApi(builder.Configuration);

// Microsoft Graph: conversion a PDF y upload a la biblioteca de documentos del sitio.
builder.Services.AddEipGraph(builder.Configuration);

// Factory explicita: el segundo parametro del builder (ruta del template) es opcional y
// solo lo usan los tests, que le pasan un template propio.
builder.Services.AddSingleton<ITicketDocumentBuilder>(sp =>
    new TicketDocumentBuilder(sp.GetRequiredService<ILogger<TicketDocumentBuilder>>()));
builder.Services.AddSingleton<ITicketAtencionDataService, TicketAtencionDataService>();
builder.Services.AddSingleton<ITicketSharePointService, TicketSharePointService>();

builder.Build().Run();
