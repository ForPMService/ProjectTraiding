using System.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.CustomFeatures.Infrastructure.DependencyInjection;
using ProjectTraiding.CustomFeatures.Options;
#if DEBUG
using ProjectTraiding.Diagnostics.DependencyInjection;
#endif
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════
// Наблюдаемость — до остальных модулей.
// ══════════════════════════════════════════════
builder.AddProjectTraidingObservability(
    activitySources: [MoexTelemetry.ActivitySourceName],
    meters: [MoexTelemetry.MeterName],
    droppedActivityNamePrefixes: [MoexTelemetry.RealtimeActivityNamePrefix]);

builder.Services.AddMoexClients(builder.Configuration);
builder.Services.AddClickHouse(builder.Configuration);
builder.Services.AddPostgre(builder.Configuration);
builder.Services.AddCustomFeatures();
builder.Services.AddCustomFeaturesCalendarSource(builder.Configuration);
builder.Services.AddMoexLoading(builder.Configuration);
builder.Services.AddMoexRealtimeReceiver(builder.Configuration);
builder.Services.AddMoexAlgopackCurrentDay(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ManagementJsonContext.Default);
});
// Граф генерации описания интерфейса собирается только в среде разработки:
// маршрут MapOpenApi отображается там же, в поставке он недостижим.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

builder.Services.AddProjectTraidingRateLimiter();

var app = builder.Build();

using (IServiceScope startupScope = app.Services.CreateScope())
{
    MoexOptions moexOptions = startupScope.ServiceProvider
        .GetRequiredService<IOptions<MoexOptions>>().Value;
    ILogger<Program> startupLogger = startupScope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();
    MoexOptionsValidator.ValidateAndLog(moexOptions, startupLogger);

    CalendarSourceOptions calendarSourceOptions = startupScope.ServiceProvider
        .GetRequiredService<IOptions<CalendarSourceOptions>>().Value;
    CalendarSourceOptionsValidator.Validate(calendarSourceOptions);
}
// Запись исключения в журнал делает сам промежуточный слой обработки: он всегда
// пишет её перед вызовом обработчика. Поэтому здесь только тело ответа.
// Осторожно при переходе на форму с регистрацией класса-обработчика: начиная
// с .NET 10 штатная запись в этой форме подавляется, и её придётся вернуть руками.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    string? traceId = Activity.Current?.Id ?? context.TraceIdentifier;

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ApiErrorDto(
            StatusCodes.Status500InternalServerError,
            "Внутренняя ошибка сервера",
            traceId),
        ManagementJsonContext.Default.ApiErrorDto);
}));

app.MapObservabilityEndpoints();

// Управление — под ведро управления (редкие команды оператора).
RouteGroupBuilder managementRoutes = app
    .MapGroup(string.Empty)
    .RequireRateLimiting(RateLimiting.ManagementPolicy);

managementRoutes.MapManagementEndpoints();

if (app.Environment.IsDevelopment())
{
#if DEBUG
    // Диагностический контур отсекается на этапе компиляции: в сборке поставки
    // ссылки на него нет, поэтому и вызова быть не может.
    app.MapDiagnosticsEndpoints();
#endif
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseHttpsRedirection();
app.Run();
