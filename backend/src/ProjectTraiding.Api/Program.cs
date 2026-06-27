using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.DependencyInjection;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Endpoints;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;
using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════
// Наблюдаемость — до остальных модулей.
// ══════════════════════════════════════════════
builder.AddProjectTraidingObservability(
    activitySources: [MoexTelemetry.ActivitySourceName],
    meters: [MoexTelemetry.MeterName]);

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.AddClickHouse(builder.Configuration);
builder.Services.AddPostgre(builder.Configuration);
builder.Services.AddTransient<MoexInstrumentWriter>();
builder.Services.AddTransient<MoexCalendarWriter>();
builder.Services.AddRawCapture(builder.Configuration);
builder.Services.AddVitrine();
builder.Services.AddManagement();
builder.Services.AddTransient<ClickHouseInsertExecutor>();
builder.Services.AddTransient<CandlesWriter>(sp => new CandlesWriter(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<ILogger<CandlesWriter>>(),
    builder.Configuration.GetValue<int>("ClickHouse:CandlesBatchSize", 10000)));
builder.Services.AddTransient<MoexLoadTaskReader>();
builder.Services.AddTransient<MoexLoadTaskWriter>();
builder.Services.AddTransient<MoexLoadedRangeWriter>();
builder.Services.AddTransient<CandlesLoadRunner>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, VitrineJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ManagementJsonContext.Default);
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapObservabilityEndpoints();
app.MapMoexSyncEndpoints();
app.MapCandlesLoadEndpoints();
app.MapManagementEndpoints();
app.MapVitrineEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapMoexDiagnosticEndpoints();
    app.MapMoexTemporaryDebugEndpoints();
    app.MapClickHouseDebugEndpoints();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();
