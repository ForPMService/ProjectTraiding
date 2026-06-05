using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Endpoints;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════
// Наблюдаемость — до остальных модулей.
// ══════════════════════════════════════════════
builder.AddProjectTraidingObservability(
    activitySources: [MoexTelemetry.ActivitySourceName],
    meters: [MoexTelemetry.MeterName]);

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.AddPostgre(builder.Configuration);
builder.Services.AddRawCapture(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapObservabilityEndpoints();
app.MapMoexProbeEndpoints();
app.MapInstrumentCardEndpoints();
app.MapMoexEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapMoexDebugEndpoints();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
