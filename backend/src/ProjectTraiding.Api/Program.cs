using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Diagnostics.DependencyInjection;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.DependencyInjection;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Endpoints;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Moex.StorageBase.Redis;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;
using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════
// Наблюдаемость — до остальных модулей.
// ══════════════════════════════════════════════
builder.AddProjectTraidingObservability(
    activitySources: [MoexTelemetry.ActivitySourceName],
    meters: [MoexTelemetry.MeterName, ApiMetrics.MeterName]);

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.AddClickHouse(builder.Configuration);
builder.Services.AddPostgre(builder.Configuration);
builder.Services.AddRedis(builder.Configuration);
builder.Services.AddTransient<MoexInstrumentWriter>();
builder.Services.AddTransient<CatalogEventPublisher>();
builder.Services.AddTransient<MoexCalendarWriter>();
builder.Services.AddTransient<MoexInstrumentBoardIntervalWriter>();
builder.Services.AddTransient<MoexTradingPeriodWriter>();
builder.Services.AddTransient<MoexTradingPeriodTypeWriter>();
builder.Services.AddTransient<MoexFuturesExpirationWriter>();
builder.Services.AddTransient<MoexSplitWriter>();
builder.Services.AddTransient<MoexManualEventWriter>();
builder.Services.AddTransient<MoexCalendarLoader>();
// Поколение данных инструмента для токена дедупликации: один читатель на оба
// потребителя — историческую загрузку и приём реального времени.
builder.Services.AddSingleton<MoexDataGenerationReader>();
builder.Services.AddVitrine(builder.Configuration);
builder.Services.AddManagement();
builder.Services.AddMoexLoading(builder.Configuration);
builder.Services.AddMoexRealtimeReceiver(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, VitrineJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ManagementJsonContext.Default);
});
builder.Services.AddOpenApi();
builder.Services.AddProjectTraidingRateLimiter();

var app = builder.Build();

using (IServiceScope startupScope = app.Services.CreateScope())
{
    MoexOptions moexOptions = startupScope.ServiceProvider
        .GetRequiredService<IOptions<MoexOptions>>().Value;
    ILogger<Program> startupLogger = startupScope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();
    MoexOptionsValidator.ValidateAndLog(moexOptions, startupLogger);
}
app.MapObservabilityEndpoints();
app.MapMoexLoadRunEndpoints();

// Управление — под ведро управления (редкие команды оператора, 1 в 30 с).
RouteGroupBuilder managementRoutes = app
    .MapGroup(string.Empty)
    .RequireRateLimiting(RateLimiting.ManagementPolicy);

managementRoutes.MapManagementEndpoints();
managementRoutes.MapMoexSyncEndpoints();

// Витрина — под ведро витрины (частый публичный поток, 1 в 2 с, ёмкость под всплеск).
app.MapGroup(string.Empty)
    .RequireRateLimiting(RateLimiting.VitrinePolicy)
    .MapVitrineEndpoints();

if (app.Environment.IsDevelopment())
{
    // Проверка среды не убирает сборку Diagnostics из поставки: ссылка Api → Diagnostics
    // безусловна, и сборка попадает в публикацию. Проверка гарантирует другое —
    // диагностические службы не регистрируются, маршруты не отображаются,
    // диагностический контекст не участвует в разрешении типов.
    app.MapMoexDiagnosticEndpoints();   // остался только календарь
    app.MapDiagnosticsEndpoints();
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseHttpsRedirection();
app.Run();
