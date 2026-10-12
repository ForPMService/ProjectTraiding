using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ProjectTraiding.Europe.Telegram;

// Owns the HTTP lifetime/poller and isolates private-owner authorization (rule 13: 1, 2 and 3).
internal sealed class EuropeTelegramBot(EuropeTelegramBotOptions options, TelegramReceiverController controller,
    ILogger<EuropeTelegramBot> logger, IMeterFactory meterFactory) : BackgroundService
{
    private readonly Histogram<double> _duration = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateHistogram<double>("telegram.bot.duration", "s");
    private readonly Counter<long> _outcomes = meterFactory.Create("ProjectTraiding.Europe.Telegram").CreateCounter<long>("telegram.bot.operations");
    private TelegramBotClient? _client;
    private long _lastRequest;
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];
    private const string Help = "/start_receiver\n/stop_receiver\n/status\n/channels\n/add_channel <username>\n/remove_channel <username>\n/help";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        using SocketsHttpHandler transport = new() { ConnectTimeout = options.RequestTimeout, AllowAutoRedirect = false };
        using HttpClient http = new(transport, disposeHandler: false) { Timeout = options.RequestTimeout };
        using ActivitySource activities = new("ProjectTraiding.Europe.Telegram.Bot");
        // Library retries are disabled: every physical request passes the same sequential throttle.
        TelegramBotClientOptions clientOptions = new(options.BotToken) { RetryCount = 0, RetryThreshold = 0 };
        _client = new TelegramBotClient(clientOptions, http, stoppingToken);
        int offset = 0;
        int consecutiveFailures = 0;
        SetState("Polling");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ThrottleAsync(stoppingToken);
                    long started = Stopwatch.GetTimestamp();
                    Update[] updates;
                    string outcome = "success";
                    using (Activity? activity = activities.StartActivity("get-updates"))
                    {
                        try
                        {
                            updates = await _client.GetUpdates(offset, limit: 100, timeout: options.PollTimeoutSeconds,
                                allowedUpdates: AllowedUpdates, cancellationToken: stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
                        catch (Exception error)
                        {
                            TelegramBotException failure = TelegramBotException.Classify(error, "get-updates");
                            outcome = failure.Category.ToString();
                            throw failure;
                        }
                        finally { Record("get-updates", outcome, started); }
                    }
                    foreach (Update update in updates)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        // Consume each update exactly once in this poller, including invalid/unauthorized input.
                        // A failed reply must not re-run an already applied Start/Add/Remove command.
                        offset = checked(update.Id + 1);
                        await HandleCommandAsync(update, stoppingToken);
                    }
                    consecutiveFailures = 0;
                    SetState("Polling");
                }
                catch (TelegramBotException failure)
                {
                    TelegramLog.Failure(logger, failure.Operation, failure.Category);
                    SetState("BotUnavailable");
                    consecutiveFailures++;
                    if (!failure.IsRetryable || consecutiveFailures >= options.MaxConsecutiveFailures) return;
                    TimeSpan delay = failure.RetryAfter ?? options.RetryDelay;
                    if (delay < options.RetryDelay) delay = options.RetryDelay;
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception)
        {
            // Unexpected poller failures are contained, without logging token-bearing library exceptions.
            TelegramLog.Failure(logger, "poller", TelegramErrorCategory.Unexpected);
            SetState("BotUnavailable");
        }
        finally
        {
            _client = null;
            if (stoppingToken.IsCancellationRequested) SetState("Stopped");
        }
    }

    private bool IsAuthorized(Update update) => update.Type == UpdateType.Message
        && update.Message is { Chat.Type: ChatType.Private, From: { } from }
        && from.Id == options.OwnerUserId && update.Message.Chat.Id == options.OwnerUserId;

    private async Task HandleCommandAsync(Update update, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(update) || update.Message?.Text is not string text) return;
        long chatId = update.Message.Chat.Id;
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string command = parts[0];
        if (parts.Length == 1)
        {
            switch (command)
            {
                case "/help": await SendAsync(chatId, Help, cancellationToken); return;
                case "/status": await SendStatusAsync(chatId, cancellationToken); return;
                case "/channels": await SendChannelsAsync(chatId, cancellationToken); return;
                case "/start_receiver":
                    await SendResultAsync(chatId, await controller.StartAsync(cancellationToken), cancellationToken); return;
                case "/stop_receiver":
                    await SendResultAsync(chatId, await controller.StopAsync(cancellationToken), cancellationToken); return;
            }
        }
        if (parts.Length == 2 && command == "/add_channel")
        {
            await SendResultAsync(chatId, await controller.AddChannelAsync(parts[1], cancellationToken), cancellationToken);
            return;
        }
        if (parts.Length == 2 && command == "/remove_channel")
        {
            await SendResultAsync(chatId, await controller.RemoveChannelAsync(parts[1], cancellationToken), cancellationToken);
            return;
        }
        await SendAsync(chatId, "InvalidInput\n" + Help, cancellationToken);
    }

    private async Task SendStatusAsync(long chatId, CancellationToken cancellationToken)
    {
        TelegramReceiverStatus status = controller.GetStatus();
        await SendAsync(chatId,
            $"Приём: {status.State}\nБот: {status.BotState}\nКаналов: {controller.GetChannels().Count}\n"
            + $"Очередь: {status.QueueCount}/{status.QueueCapacity}\nПринято: {status.AcceptedCount}\n"
            + $"Переслано: {status.ForwardedCount}; отказов: {status.ForwardFailureCount}\n"
            + $"Переполнений: {status.OverflowCount}; возможных потерь: {status.PossiblyLostCount}\n"
            + $"Последний приём UTC: {status.LastReceivedAtUtc:O}\nДренирование завершено: {status.DrainCompleted}\n"
            + $"Очистка ожидается: {status.CleanupPending}\nОшибка: {status.LastError ?? "нет"}\n"
            + "Сохранность публикаций при аварии пока не обеспечена.", cancellationToken);
    }

    private async Task SendChannelsAsync(long chatId, CancellationToken cancellationToken)
    {
        IReadOnlyList<TelegramChannelStatus> channels = controller.GetChannels();
        if (channels.Count == 0) { await SendAsync(chatId, "Каналы не настроены.", cancellationToken); return; }
        StringBuilder message = new();
        foreach (TelegramChannelStatus channel in channels)
        {
            string line = $"@{channel.Username}: {(channel.Available ? "доступен" : "не проверен/приём остановлен")}\n";
            // Stay below Bot API's 4096-character message limit, without accumulating an unbounded reply.
            if (message.Length + line.Length > 4000)
            {
                await SendAsync(chatId, message.ToString(), cancellationToken);
                message.Clear();
            }
            message.Append(line);
        }
        if (message.Length > 0) await SendAsync(chatId, message.ToString(), cancellationToken);
    }

    private Task SendResultAsync(long chatId, TelegramReceiverCommandResult result, CancellationToken cancellationToken)
        => SendAsync(chatId, $"{result.Outcome}\n{result.ErrorCode}\n{result.Message}", cancellationToken);

    private async Task SendAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken);
        long started = Stopwatch.GetTimestamp();
        string outcome = "success";
        try
        {
            await _client!.SendMessage(chatId, text, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; throw; }
        catch (Exception error)
        {
            TelegramBotException failure = TelegramBotException.Classify(error, "send-message");
            outcome = failure.Category.ToString();
            throw failure;
        }
        finally { Record("send-message", outcome, started); }
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        // Telegram's per-chat send rate is respected by a single shared physical-request cadence.
        if (_lastRequest != 0)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(_lastRequest);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
        }
        _lastRequest = Stopwatch.GetTimestamp();
    }

    private void SetState(string state)
    {
        if (controller.GetStatus().BotState == state) return;
        controller.SetBotState(state);
        TelegramLog.BotState(logger, state);
    }

    private void Record(string operation, string outcome, long started)
    {
        _duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new("operation", operation), new("outcome", outcome));
        _outcomes.Add(1, new("operation", operation), new("outcome", outcome));
    }
}

internal sealed class TelegramBotException(TelegramErrorCategory category, string operation,
    bool isRetryable = false, TimeSpan? retryAfter = null, int? sourceCode = null)
    : TelegramBoundaryException(category, operation, isRetryable, retryAfter, sourceCode)
{
    internal static TelegramBotException Classify(Exception error, string operation)
    {
        if (error is ApiRequestException api)
            return api.ErrorCode switch
            {
                429 => new(TelegramErrorCategory.RateLimited, operation, true,
                    api.Parameters?.RetryAfter is int seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null, api.ErrorCode),
                401 => new(TelegramErrorCategory.InvalidConfiguration, operation, false, null, api.ErrorCode),
                403 => new(TelegramErrorCategory.AccessDeniedOrProtected, operation, false, null, api.ErrorCode),
                _ => new(TelegramErrorCategory.TransportUnavailable, operation, api.ErrorCode >= 500, null, api.ErrorCode)
            };
        if (error is OperationCanceledException or TimeoutException)
            return new(TelegramErrorCategory.NetworkTimeout, operation, true);
        if (error is HttpRequestException or RequestException)
            return new(TelegramErrorCategory.TransportUnavailable, operation, true);
        return new(TelegramErrorCategory.Unexpected, operation);
    }
}
