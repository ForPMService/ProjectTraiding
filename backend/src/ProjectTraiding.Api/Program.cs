using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
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
// Свечные карты по интервалам: одна форма, разные таблица и префикс токена.
// Коды интервала MOEX: 1 — минута, 10 — десять минут, 60 — час, 24 — день.
builder.Services.AddSingleton<IReadOnlyDictionary<int, RowWriter<CandlesDTO>>>(sp =>
{
    ClickHouseInsertExecutor executor = sp.GetRequiredService<ClickHouseInsertExecutor>();
    ILogger<RowWriter<CandlesDTO>> logger = sp.GetRequiredService<ILogger<RowWriter<CandlesDTO>>>();
    int candlesBatchSize = builder.Configuration.GetValue<int>("ClickHouse:CandlesBatchSize", 10000);

    RowWriter<CandlesDTO> Make(string table, string tokenPrefix) =>
        new RowWriter<CandlesDTO>(executor, new CandlesRowMap(table, tokenPrefix), logger, candlesBatchSize);

    return new Dictionary<int, RowWriter<CandlesDTO>>
    {
        [1] = Make("moex_candles_1m", "candles:1m"),
        [10] = Make("moex_candles_10m", "candles:10m"),
        [60] = Make("moex_candles_1h", "candles:1h"),
        [24] = Make("moex_candles_1d", "candles:1d"),
    };
});
// Карты столбцов остальных видов (без состояния — одиночки).
builder.Services.AddSingleton<TradeStatsStockRowMap>();
builder.Services.AddSingleton<TradeStatsFuturesRowMap>();
builder.Services.AddSingleton<ObStatsStockRowMap>();
builder.Services.AddSingleton<ObStatsFuturesRowMap>();
builder.Services.AddSingleton<OrderStatsStockRowMap>();
builder.Services.AddSingleton<FutoiRowMap>();
builder.Services.AddSingleton<Hi2StockRowMap>();
builder.Services.AddSingleton<Hi2FuturesRowMap>();
builder.Services.AddSingleton<MegaAlertsStockRowMap>();
builder.Services.AddSingleton<MegaAlertsFuturesRowMap>();

// Писатели под каждый вид (тот же размер пачки, что у свечей).
int statsBatchSize = builder.Configuration.GetValue<int>("ClickHouse:CandlesBatchSize", 10000);
builder.Services.AddTransient(sp => new RowWriter<SuperCandlesTradeStats5mDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<TradeStatsStockRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<SuperCandlesTradeStats5mDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<SuperCandlesFuturesTradeStats5mDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<TradeStatsFuturesRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<SuperCandlesFuturesTradeStats5mDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<SuperCandlesOrderBookStats5mDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<ObStatsStockRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<SuperCandlesOrderBookStats5mDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<SuperCandlesFuturesOrderBookStats5mDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<ObStatsFuturesRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<SuperCandlesFuturesOrderBookStats5mDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<SuperCandlesOrderStats5mDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<OrderStatsStockRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<SuperCandlesOrderStats5mDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<FutoiDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<FutoiRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<FutoiDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<Hi2AssetDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<Hi2StockRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<Hi2AssetDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<Hi2FuturesDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<Hi2FuturesRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<Hi2FuturesDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<MegaAlertsAssetsDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<MegaAlertsStockRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<MegaAlertsAssetsDTO>>>(), statsBatchSize));
builder.Services.AddTransient(sp => new RowWriter<MegaAlertsFuturesDTO>(
    sp.GetRequiredService<ClickHouseInsertExecutor>(),
    sp.GetRequiredService<MegaAlertsFuturesRowMap>(),
    sp.GetRequiredService<ILogger<RowWriter<MegaAlertsFuturesDTO>>>(), statsBatchSize));

// Обработчики остальных видов (диспетчер соберёт их все через IEnumerable<ILoadHandler>).
builder.Services.AddScoped<ILoadHandler, TradeStatsStockLoadHandler>();
builder.Services.AddScoped<ILoadHandler, TradeStatsFuturesLoadHandler>();
builder.Services.AddScoped<ILoadHandler, ObStatsStockLoadHandler>();
builder.Services.AddScoped<ILoadHandler, ObStatsFuturesLoadHandler>();
builder.Services.AddScoped<ILoadHandler, OrderStatsStockLoadHandler>();
builder.Services.AddScoped<ILoadHandler, FutoiLoadHandler>();
builder.Services.AddScoped<ILoadHandler, Hi2StockLoadHandler>();
builder.Services.AddScoped<ILoadHandler, Hi2FuturesLoadHandler>();
builder.Services.AddScoped<ILoadHandler, MegaAlertsStockLoadHandler>();
builder.Services.AddScoped<ILoadHandler, MegaAlertsFuturesLoadHandler>();

builder.Services.AddTransient<MoexLoadTaskReader>();
builder.Services.AddTransient<MoexLoadTaskWriter>();
builder.Services.AddTransient<MoexLoadedRangeWriter>();
builder.Services.AddScoped<ILoadHandler, CandlesLoadHandler>();
builder.Services.AddScoped<LoadHandlerDispatcher>();
builder.Services.AddScoped<LoadRunner>();
builder.Services.AddHostedService(sp =>
{
    MoexOptions moexOptions = sp.GetRequiredService<IOptions<MoexOptions>>().Value;
    return new CandlesLoadBackgroundService(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<CandlesLoadBackgroundService>>(),
        TimeSpan.FromSeconds(moexOptions.PollIntervalSeconds),
        moexOptions.LoadWorkerConcurrency);
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, VitrineJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ManagementJsonContext.Default);
});
builder.Services.AddOpenApi();

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
}
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
