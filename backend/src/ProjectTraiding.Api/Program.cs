using System.Diagnostics;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.Options;
using ProjectTraiding.Api.Infrastructure;
using ProjectTraiding.Api.Telegram;
using ProjectTraiding.CustomFeatures.Infrastructure.DependencyInjection;
using ProjectTraiding.CustomFeatures.Messaging;
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
using ProjectTraiding.Telegram;
using Wolverine;

bool isCodeGenerationCommand = args.Length > 0
    && string.Equals(args[0], "codegen", StringComparison.OrdinalIgnoreCase);
#if !DEBUG
if (isCodeGenerationCommand)
{
    Console.Error.WriteLine("Wolverine code generation commands are available only in Debug configuration.");
    return 1;
}
#endif

bool isWolverineSmoke = string.Equals(
    Environment.GetEnvironmentVariable("PROJECTTRAIDING_WOLVERINE_SMOKE"),
    "1",
    StringComparison.Ordinal);
bool isIsolatedWolverineRun = isCodeGenerationCommand || isWolverineSmoke;

var builder = WebApplication.CreateBuilder(args);

if (isWolverineSmoke)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

// ══════════════════════════════════════════════
// Наблюдаемость — до остальных модулей.
// ══════════════════════════════════════════════
if (!isIsolatedWolverineRun)
{
    builder.AddProjectTraidingObservability(
        activitySources: [MoexTelemetry.ActivitySourceName],
        meters: [MoexTelemetry.MeterName],
        droppedActivityNamePrefixes: [MoexTelemetry.RealtimeActivityNamePrefix]);
}

if (!isIsolatedWolverineRun)
{
    builder.Services.AddMoexClients(builder.Configuration);
    builder.Services.AddClickHouse(builder.Configuration);
    builder.Services.AddPostgre(builder.Configuration);
    builder.Services.AddCustomFeatures();
    builder.Services.AddCustomFeaturesCalendarSource(builder.Configuration);
    builder.Services.AddMoexLoading(builder.Configuration);
    builder.Services.AddMoexRealtimeReceiver(builder.Configuration);
    builder.Services.AddMoexAlgopackCurrentDay(builder.Configuration);
    builder.Services.AddTelegram(builder.Configuration);
}
else
{
    builder.Services.AddSingleton<CustomFeaturesWolverineSmokeAcknowledgement>();
    builder.Services.AddHealthChecks();

    if (isCodeGenerationCommand)
    {
        builder.Services.AddOptions<MoexOptions>()
            .Bind(builder.Configuration.GetSection("Moex"));
        builder.Services.AddOptions<CalendarSourceOptions>()
            .Bind(builder.Configuration.GetSection("Moex"));
    }
}

builder.Host.UseWolverine(options =>
{
    options.ApplicationAssembly = typeof(Program).Assembly;
    options.Discovery
        .DisableConventionalDiscovery()
        .IncludeType<CustomFeaturesWolverineSmokeHandler>();
    options.PublishMessage<CustomFeaturesWolverineSmokeMessage>()
        .ToLocalQueue("custom-features-smoke");

    if (!isCodeGenerationCommand)
    {
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
        options.Services.CritterStackDefaults(defaults =>
        {
            defaults.Development.AssertAllPreGeneratedTypesExist = true;
            defaults.Production.AssertAllPreGeneratedTypesExist = true;
        });
    }
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

if (!isWolverineSmoke)
{
    using IServiceScope startupScope = app.Services.CreateScope();
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
if (isWolverineSmoke)
{
    return await RunWolverineSmokeAsync(app);
}

#if DEBUG
return await app.RunJasperFxCommands(args);
#else
await app.RunAsync();
return 0;
#endif

static async Task<int> RunWolverineSmokeAsync(WebApplication app)
{
    ILogger logger = app.Logger;
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
    bool hostStarted = false;
    int exitCode = 0;

    try
    {
        await app.StartAsync(timeout.Token);
        hostStarted = true;

        using IServiceScope scope = app.Services.CreateScope();
        IMessageBus messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        CustomFeaturesWolverineSmokeAcknowledgement acknowledgement = scope.ServiceProvider
            .GetRequiredService<CustomFeaturesWolverineSmokeAcknowledgement>();
        Guid messageId = Guid.NewGuid();
        Task<Guid> processedMessage = acknowledgement.WaitForCompletionAsync(timeout.Token);

        await messageBus.SendAsync(new CustomFeaturesWolverineSmokeMessage(messageId));

        Guid confirmedMessageId = await processedMessage;
        if (confirmedMessageId != messageId)
        {
            throw new InvalidOperationException(
                "Wolverine smoke handler confirmed a different message identifier.");
        }

        WolverineSmokeLogMessages.MessageConfirmed(logger, messageId);
    }
    catch (Exception exception)
    {
        WolverineSmokeLogMessages.ExecutionFailed(logger, exception);
        exitCode = 1;
    }
    finally
    {
        if (hostStarted)
        {
            using CancellationTokenSource stopTimeout = new(TimeSpan.FromSeconds(5));
            try
            {
                await app.StopAsync(stopTimeout.Token);
            }
            catch (Exception exception)
            {
                WolverineSmokeLogMessages.HostShutdownFailed(logger, exception);
                exitCode = 1;
            }
        }

        await app.DisposeAsync();
    }

    return exitCode;
}
