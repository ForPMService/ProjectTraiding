using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Diagnostics.DependencyInjection;
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Loading;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;

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
builder.Services.AddTransient<MoexCalendarWriter>();
builder.Services.AddTransient<MoexCalendarReferenceWriter>();
builder.Services.AddTransient<MoexCalendarLoader>();
// Поколение данных инструмента для токена дедупликации: один читатель на оба
// потребителя — историческую загрузку и приём реального времени.
builder.Services.AddSingleton<MoexDataGenerationReader>();
builder.Services.AddManagement();
builder.Services.AddMoexLoading(builder.Configuration);
builder.Services.AddMoexRealtimeReceiver(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
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

// Управление — под ведро управления (редкие команды оператора).
RouteGroupBuilder managementRoutes = app
    .MapGroup(string.Empty)
    .RequireRateLimiting(RateLimiting.ManagementPolicy);

managementRoutes.MapManagementEndpoints();

if (app.Environment.IsDevelopment())
{
    // Проверка среды не убирает сборку Diagnostics из поставки: ссылка Api → Diagnostics
    // безусловна, и сборка попадает в публикацию. Проверка гарантирует другое —
    // диагностические службы не регистрируются, маршруты не отображаются,
    // диагностический контекст не участвует в разрешении типов.
    app.MapDiagnosticsEndpoints();
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseHttpsRedirection();
app.Run();
