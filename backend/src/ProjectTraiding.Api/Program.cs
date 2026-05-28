using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════
// Наблюдаемость — до остальных модулей.
// Строковые литералы заменятся на MoexTelemetry.ActivitySourceName / MeterName
// после создания MoexTelemetry (задача 4).
// ══════════════════════════════════════════════
builder.AddProjectTraidingObservability(
    activitySources: ["ProjectTraiding.Moex"],
    meters: ["ProjectTraiding.Moex"]);

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.AddRawCapture(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapObservabilityEndpoints();
app.MapMoexEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapMoexDebugEndpoints();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
