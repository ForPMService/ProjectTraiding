using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Diagnostics.Contracts;
using ProjectTraiding.Diagnostics.DependencyInjection;
using ProjectTraiding.Diagnostics.Options;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.DependencyInjection;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Serialization;
using ProjectTraiding.Moex.Endpoints;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
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
builder.Services.AddRawCapture(builder.Configuration);
builder.Services.AddVitrine(builder.Configuration);
builder.Services.AddManagement();
builder.Services.AddMoexLoading(builder.Configuration);
builder.Services.AddMoexRealtimeStorage(builder.Configuration);
builder.Services.AddMoexRealtimeReceiver(builder.Configuration);

if (builder.Environment.IsDevelopment())
    builder.Services.AddProjectTraidingDiagnostics(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, VitrineJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ManagementJsonContext.Default);

    // Диагностический контекст добавляется В КОНЕЦ цепочки, а не в начало. Общие типы
    // (например CandlesDTO) объявлены в обоих контекстах; при вставке в начало
    // диагностический перехватывал бы их в среде разработки, и поведение сериализации
    // расходилось бы со средой боевой работы. В конце цепочки он закрывает только те типы,
    // которых в боевых контекстах нет.
    if (builder.Environment.IsDevelopment())
        options.SerializerOptions.TypeInfoResolverChain.Add(DiagnosticsJsonContext.Default);
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

    RawCaptureOptions rawCaptureOptions = startupScope.ServiceProvider
        .GetRequiredService<IOptions<RawCaptureOptions>>().Value;
    RawCaptureOptionsValidator.Validate(rawCaptureOptions);

    if (app.Environment.IsDevelopment())
    {
        WebSocketProbeOptions probeOptions = startupScope.ServiceProvider
            .GetRequiredService<IOptions<WebSocketProbeOptions>>().Value;
        WebSocketProbeOptionsValidator.Validate(probeOptions);
    }
}
app.MapObservabilityEndpoints();
app.MapMoexSyncEndpoints();
app.MapMoexLoadRunEndpoints();

// Управление — под ведро управления (редкие команды оператора, 1 в 30 с).
app.MapGroup(string.Empty)
    .RequireRateLimiting(RateLimiting.ManagementPolicy)
    .MapManagementEndpoints();

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
