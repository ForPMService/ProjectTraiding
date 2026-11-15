using System.Diagnostics;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Api.Telegram;
using ProjectTraiding.CustomFeatures.Infrastructure.DependencyInjection;
using ProjectTraiding.CustomFeatures.Options;
using ProjectTraiding.CustomFeatures.Realtime;
#if DEBUG
using ProjectTraiding.Diagnostics.DependencyInjection;
#endif
using ProjectTraiding.Management.Contracts;
using ProjectTraiding.Management.Contracts.Dto;
using ProjectTraiding.Management.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.DependencyInjection;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Options;
using ProjectTraiding.Moex.Realtime.Subscriptions;
using ProjectTraiding.Moex.StorageBase.Postgres;
using ProjectTraiding.Observability.Infrastructure.DependencyInjection;
using ProjectTraiding.Telegram;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
bool isWolverineCodeGeneration = args.Length >= 2
    && string.Equals(args[0], "codegen", StringComparison.OrdinalIgnoreCase)
    && string.Equals(args[1], "write", StringComparison.OrdinalIgnoreCase);

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
if (!isWolverineCodeGeneration)
{
    // Code generation only needs Wolverine's application model. Do not start market,
    // PostgreSQL-backed or Telegram background workers as a side effect of the CLI.
    builder.Services.AddMoexLoading(builder.Configuration);
    builder.Services.AddMoexRealtimeReceiver(builder.Configuration);
    builder.Services.AddMoexAlgopackCurrentDay(builder.Configuration);
    builder.Services.AddTelegram(builder.Configuration);
}

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(CandleAcceptedHandler).Assembly);
    options.LocalQueueFor<CandleAccepted>().Sequential();
#if DEBUG
    options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
#else
    options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
#endif
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ManagementJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, TelegramAuthJsonContext.Default);
});
// Граф генерации описания интерфейса собирается только в среде разработки:
// маршрут MapOpenApi отображается там же, в поставке он недостижим.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

builder.Services.AddProjectTraidingRateLimiter();

var app = builder.Build();
using IServiceScope startupScope = app.Services.CreateScope();
MoexOptions moexOptions = startupScope.ServiceProvider
    .GetRequiredService<IOptions<MoexOptions>>().Value;
ILogger<Program> startupLogger = startupScope.ServiceProvider
    .GetRequiredService<ILogger<Program>>();
MoexOptionsValidator.ValidateAndLog(moexOptions, startupLogger);

CalendarSourceOptions calendarSourceOptions = startupScope.ServiceProvider
    .GetRequiredService<IOptions<CalendarSourceOptions>>().Value;
CalendarSourceOptionsValidator.Validate(calendarSourceOptions);
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
app.MapTelegramAuthorizationEndpoints();

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
#if DEBUG
return await app.RunJasperFxCommands(args);
#else
await app.RunAsync();
return 0;
#endif
