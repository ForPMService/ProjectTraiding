using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTraiding.Europe.Telegram;

namespace ProjectTraiding.Europe.News;

/// <summary>Локальная управляемая проверка замены контекста и клиента без сети и без API-вызовов.</summary>
internal static class NewsAgentContextChecks
{
    internal static string CreateExampleJson() => JsonSerializer.Serialize(
        CreateSnapshot("news-v1", "prompt-v1", "Analyze the publication using the supplied context.", ["SYNTH"], 1),
        NewsAgentJsonContext.Default.NewsAgentPromptSnapshot);

    internal static async Task RunAsync()
    {
        NewsAgentPromptSnapshot emittedExample = JsonSerializer.Deserialize(CreateExampleJson(),
            NewsAgentJsonContext.Default.NewsAgentPromptSnapshot)
            ?? throw new InvalidOperationException("CLI-пример News не прошёл десериализацию.");
        NewsAgentExecutor.ValidatePromptSnapshot(emittedExample);

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["News:Enabled"] = "true",
                ["News:QueueCapacity"] = "1",
                ["News:Workers"] = "1",
                ["News:RequestTimeout"] = "00:00:30",
                ["LLM:ActiveProvider"] = "OpenAI",
                ["OpenAI:ApiKey"] = "local-openai-key",
                ["OpenAI:ActiveProfile"] = "default",
                ["OpenAI:Profiles:default:Model"] = "gpt-6-luna",
                ["OpenAI:Profiles:default:ReasoningEffort"] = "None",
                ["OpenAI:Profiles:gpt41:Model"] = "gpt-4.1-2025-04-14",
                ["XAI:Endpoint"] = "https://api.x.ai/v1",
                ["XAI:ActiveProfile"] = "default",
                ["XAI:Profiles:default:Model"] = "grok-4.7",
                ["XAI:Profiles:default:ReasoningEffort"] = "High",
                ["XAI:Profiles:grok46:Model"] = "grok-4.6",
                ["XAI:Profiles:grok46:ReasoningEffort"] = "High"
            })
            .Build();

        await RunMetricsChecksAsync(configuration);

        NewsAgentOptions options = NewsAgentOptions.Load(configuration);
        ControlledChatClient openAiClient = new("OpenAI");
        ControlledChatClient xaiClient = new("XAI");
        EuropeTelegramRuntime telegram = new(new TelegramOptions(), NullLogger<EuropeTelegramRuntime>.Instance);
        NewsAgentExecutor executor = new(telegram, options, NullLogger<NewsAgentExecutor>.Instance,
            null!, configuration, openAiClient, xaiClient);

        try
        {
            if (configuration["XAI:ApiKey"] is not null)
                throw new InvalidOperationException("Активный OpenAI не должен требовать ключ XAI.");

            NewsAgentPromptSnapshot oldSubject = CreateSnapshot("snapshot-a", "prompt-a", "Synthetic prompt A",
                ["A", "B", "C"], 1);
            if (executor.GetSnapshotStatus() is not { HasSnapshot: false, WaitingForFirstSnapshot: true })
                throw new InvalidOperationException("Статус до первого снимка задан неверно.");
            if (executor.UpdatePromptSnapshot(oldSubject) != NewsSnapshotApplyStatus.Applied)
                throw new InvalidOperationException("Первый снимок не был применён.");
            string oldVersion = executor.CurrentExecutionSnapshot!.AgentVersion;
            if (executor.UpdatePromptSnapshot(oldSubject) != NewsSnapshotApplyStatus.AlreadyApplied)
                throw new InvalidOperationException("Точный повтор не распознан.");
            NewsAgentPromptSnapshot sameRevisionConflict = oldSubject with { Prompt = "Different prompt" };
            if (executor.UpdatePromptSnapshot(sameRevisionConflict) != NewsSnapshotApplyStatus.Conflict
                || executor.CurrentExecutionSnapshot!.Subject.Prompt != oldSubject.Prompt)
                throw new InvalidOperationException("Конфликт одной редакции заменил снимок.");

            NewsAgentPromptSnapshot sameContentNewRevision = oldSubject with { Revision = 2 };
            if (executor.UpdatePromptSnapshot(sameContentNewRevision) != NewsSnapshotApplyStatus.Applied
                || executor.CurrentExecutionSnapshot!.AgentVersion != oldVersion)
                throw new InvalidOperationException("Номер редакции изменил agent_version.");
            if (executor.UpdatePromptSnapshot(oldSubject) != NewsSnapshotApplyStatus.Stale)
                throw new InvalidOperationException("Старая редакция не распознана.");
            foreach (long? invalidRevision in new long?[] { 0, -1, null })
            {
                try
                {
                    executor.UpdatePromptSnapshot(oldSubject with { Revision = invalidRevision });
                    throw new InvalidOperationException("Неподдерживаемая редакция была принята.");
                }
                catch (ArgumentException)
                {
                }
                if (executor.CurrentExecutionSnapshot!.Subject.Revision != 2)
                    throw new InvalidOperationException("Отклонённая редакция заменила последний снимок.");
            }
            using JsonDocument reorderedSchema = CreateReorderedSchema();
            if (executor.UpdatePromptSnapshot(sameContentNewRevision with { JsonSchema = reorderedSchema.RootElement })
                != NewsSnapshotApplyStatus.AlreadyApplied)
                throw new InvalidOperationException("Семантически одинаковая JSON Schema не распознана.");

            TelegramChannelPost telegramPost = new(17, "sample_channel", "Sample", 23,
                "Original Telegram text", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            NewsPublicationInput telegramInput = NewsAgentExecutor.ConvertTelegramPublication(telegramPost);
            if (telegramInput.Source != "Telegram" || telegramInput.RawText != telegramPost.Text
                || telegramInput.PublishedAt != telegramPost.PublishedAt
                || telegramInput.ReceivedAt != telegramPost.ReceivedAt
                || telegramInput.SourceMetadata.GetProperty("channel_id").GetInt64() != 17
                || telegramInput.SourceMetadata.GetProperty("message_id").GetInt32() != 23
                || telegramInput.SourceMetadata.TryGetProperty("text", out _))
                throw new InvalidOperationException("Telegram не преобразован в общий контракт без потерь и дублирования.");
            NewsAgentResult result = new(telegramInput, "{}", "snapshot", "model", "agent");
            string serializedResult = JsonSerializer.Serialize(result, NewsAgentJsonContext.Default.NewsAgentResult);
            using JsonDocument resultJson = JsonDocument.Parse(serializedResult);
            if (resultJson.RootElement.GetProperty("publication").GetProperty("source_metadata")
                .GetProperty("channel_id").GetInt64() != 17)
                throw new InvalidOperationException("Сериализация NewsAgentResult потеряла общий контракт публикации.");

            Task oldOpenAiRequest = executor.AnalyzeForDiagnosticsAsync(telegramInput, CancellationToken.None);
            ControlledChatClient.CapturedCall oldOpenAiCall = await openAiClient.NextCallAsync();
            AssertCall(oldOpenAiCall, "OpenAI", "gpt-6-luna", "Synthetic prompt A", ["A", "B", "C"],
                ReasoningEffort.None);
            AssertPublication(oldOpenAiCall, telegramInput);

            NewsAgentPromptSnapshot newSubject = CreateSnapshot("snapshot-b", "prompt-b", "Synthetic prompt B",
                ["A", "C", "D"], 3);
            if (executor.UpdatePromptSnapshot(newSubject) != NewsSnapshotApplyStatus.Applied)
                throw new InvalidOperationException("Новая редакция не была применена.");
            configuration["OpenAI:ActiveProfile"] = "gpt41";
            configuration.Reload();

            NewsAgentExecutionSnapshot newExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После обновления отсутствует рабочая конфигурация News.");
            if (newExecution.AgentVersion == oldVersion || newExecution.Profile.Model != "gpt-4.1-2025-04-14")
                throw new InvalidOperationException("agent_version не отражает активный профиль и снимок.");

            Task newOpenAiRequest = executor.AnalyzeForDiagnosticsAsync(telegramInput, CancellationToken.None);
            ControlledChatClient.CapturedCall newOpenAiCall = await openAiClient.NextCallAsync();
            AssertCall(newOpenAiCall, "OpenAI", "gpt-4.1-2025-04-14", "Synthetic prompt B", ["A", "C", "D"], null);

            configuration["OpenAI:ApiKey"] = null;
            configuration["XAI:ApiKey"] = "local-xai-key";
            configuration["LLM:ActiveProvider"] = "XAI";
            configuration["XAI:ActiveProfile"] = "default";
            configuration.Reload();
            NewsAgentOptions xaiOptions = NewsAgentOptions.Load(configuration);
            if (!xaiOptions.Enabled)
                throw new InvalidOperationException("Активный XAI профиль не загрузил настройки News.");

            NewsAgentExecutionSnapshot xaiExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После переключения отсутствует рабочая конфигурация News.");
            if (xaiExecution.Profile.Provider != "XAI" || xaiExecution.Profile.Model != "grok-4.7"
                || xaiExecution.AgentVersion == newExecution.AgentVersion)
                throw new InvalidOperationException("Снимок не переключился на независимый профиль XAI.");

            Task oldXaiRequest = executor.AnalyzeForDiagnosticsAsync(telegramInput, CancellationToken.None);
            ControlledChatClient.CapturedCall oldXaiCall = await xaiClient.NextCallAsync();
            AssertCall(oldXaiCall, "XAI", "grok-4.7", "Synthetic prompt B", ["A", "C", "D"],
                ReasoningEffort.High);

            NewsAgentPromptSnapshot newestSubject = CreateSnapshot("snapshot-c", "prompt-c", "Synthetic prompt C",
                ["A", "D", "E"], 4);
            executor.UpdatePromptSnapshot(newestSubject);
            configuration["XAI:ActiveProfile"] = "grok46";
            configuration.Reload();

            NewsAgentExecutionSnapshot newestExecution = executor.CurrentExecutionSnapshot
                ?? throw new InvalidOperationException("После смены модели XAI отсутствует рабочая конфигурация.");
            if (newestExecution.Profile.Provider != "XAI" || newestExecution.Profile.Model != "grok-4.6")
                throw new InvalidOperationException("Смена модели XAI не обновила целый снимок.");

            Task newXaiRequest = executor.AnalyzeForDiagnosticsAsync(telegramInput, CancellationToken.None);
            ControlledChatClient.CapturedCall newXaiCall = await xaiClient.NextCallAsync();
            AssertCall(newXaiCall, "XAI", "grok-4.6", "Synthetic prompt C", ["A", "D", "E"],
                ReasoningEffort.High);

            if (openAiClient.DisposeCount != 0 || xaiClient.DisposeCount != 0)
                throw new InvalidOperationException("Клиент освобождён, пока старые запросы ещё выполняются.");

            string currentVersion = newestExecution.AgentVersion;
            NewsAgentPromptSnapshot invalid = newestSubject with { Prompt = " " };
            try
            {
                executor.UpdatePromptSnapshot(invalid);
                throw new InvalidOperationException("Некорректный снимок был принят.");
            }
            catch (ArgumentException)
            {
            }
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Отклонённый снимок заменил последнюю рабочую конфигурацию.");

            try
            {
                executor.UpdatePromptSnapshot(newestSubject with { Temperature = 1.1f, Revision = 5 });
                throw new InvalidOperationException("XAI принял недопустимый Temperature.");
            }
            catch (InvalidOperationException error) when (error.Message == "Для XAI News Temperature не должна превышать 1.")
            {
            }
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Недопустимый XAI Temperature заменил последнюю рабочую конфигурацию.");

            configuration["XAI:Profiles:grok46:ReasoningEffort"] = "unsupported";
            configuration.Reload();
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Некорректный ReasoningEffort заменил рабочую конфигурацию.");

            configuration["XAI:Profiles:grok46:ReasoningEffort"] = "High";
            configuration["XAI:ApiKey"] = null;
            configuration.Reload();
            if (executor.CurrentExecutionSnapshot!.AgentVersion != currentVersion)
                throw new InvalidOperationException("Профиль без активного ключа заменил рабочую конфигурацию.");

            openAiClient.Release(oldOpenAiCall.Number);
            openAiClient.Release(newOpenAiCall.Number);
            xaiClient.Release(oldXaiCall.Number);
            xaiClient.Release(newXaiCall.Number);
            await ExpectControlledFailureAsync(oldOpenAiRequest);
            await ExpectControlledFailureAsync(newOpenAiRequest);
            await ExpectControlledFailureAsync(oldXaiRequest);
            await ExpectControlledFailureAsync(newXaiRequest);
        }
        finally
        {
            openAiClient.ReleaseAll();
            xaiClient.ReleaseAll();
            executor.Dispose();
            telegram.Dispose();
        }

        if (openAiClient.DisposeCount != 1 || xaiClient.DisposeCount != 1)
            throw new InvalidOperationException("Каждый управляемый IChatClient должен освобождаться при закрытии исполнителя.");

        IConfigurationRoot disabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["News:Enabled"] = "false",
                ["LLM:ActiveProvider"] = "OpenAI",
                ["OpenAI:ActiveProfile"] = "default",
                ["OpenAI:Profiles:default:Model"] = "gpt-6-luna"
            })
            .Build();
        NewsAgentOptions disabledOptions = NewsAgentOptions.Load(disabledConfiguration);
        if (disabledOptions.Enabled)
            throw new InvalidOperationException("Выключенный контур News не должен требовать настройки провайдера.");
        ControlledChatClient disabledClient = new("OpenAI");
        EuropeTelegramRuntime disabledTelegram = new(new TelegramOptions(),
            NullLogger<EuropeTelegramRuntime>.Instance);
        NewsAgentExecutor disabledExecutor = new(disabledTelegram, disabledOptions,
            NullLogger<NewsAgentExecutor>.Instance, null!, disabledConfiguration, disabledClient);
        try
        {
            if (disabledExecutor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "disabled")))
                throw new InvalidOperationException("Выключенный контур News принял публикацию в очередь.");
            if (disabledClient.CallCount != 0)
                throw new InvalidOperationException("Выключенный контур News вызвал клиента.");
        }
        finally
        {
            disabledExecutor.Dispose();
            disabledTelegram.Dispose();
        }

        await RunWorkerGateChecksAsync();
    }

    private static async Task RunMetricsChecksAsync(IConfigurationRoot configuration)
    {
        using MetricsCapture capture = new();
        NewsAgentOptions options = new()
        {
            Enabled = true,
            QueueCapacity = 1,
            Workers = 1,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
        ControlledChatClient client = new("OpenAI");
        using IHost host = BuildManagedHost(configuration, options, client);
        await host.StartAsync();
        NewsAgentExecutor executor = host.Services.GetRequiredService<NewsAgentExecutor>();
        if (!executor.TryEnqueuePublication(CreateSyntheticPublication("Telegram", "metric queued"))
            || executor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "metric rejected")))
            throw new InvalidOperationException("Метрики не должны менять ограниченную постановку в очередь.");
        capture.Listener.RecordObservableInstruments();
        capture.AssertLast("projecttraiding.europe.news.queue.depth", 1);
        capture.AssertLast("projecttraiding.europe.news.queue.capacity", 1);
        capture.AssertCount("projecttraiding.europe.news.queue.accepted", 1);
        capture.AssertMeasurement("projecttraiding.europe.news.queue.accepted", "source=Telegram", 1);
        capture.AssertMeasurement("projecttraiding.europe.news.queue.rejected", "reason=queue_unavailable", 1);
        capture.AssertMeasurement("projecttraiding.europe.news.queue.rejected", "source=Bigdata", 1);

        using (JsonDocument invalidMetadata = JsonDocument.Parse("[]"))
        {
            AssertInvalidInput(executor, new NewsPublicationInput("unknown-source", "invalid", null,
                DateTimeOffset.UnixEpoch, invalidMetadata.RootElement));
        }
        capture.AssertMeasurement("projecttraiding.europe.news.queue.rejected", "reason=invalid", 1);
        capture.AssertMeasurement("projecttraiding.europe.news.queue.rejected", "source=other", 1);

        if (executor.UpdatePromptSnapshot(CreateSnapshot("metrics", "metrics", "Metrics prompt", ["M"], 1))
            != NewsSnapshotApplyStatus.Applied)
            throw new InvalidOperationException("Не удалось запустить управляемую попытку для проверки метрик.");
        ControlledChatClient.CapturedCall call = await client.NextCallAsync();
        capture.AssertMeasurement("projecttraiding.europe.news.processing.active", "source=Telegram", 1);
        capture.Listener.RecordObservableInstruments();
        capture.AssertLast("projecttraiding.europe.news.queue.depth", 0);
        client.Release(call.Number);
        await host.StopAsync(TimeSpan.FromSeconds(2));
        capture.AssertMeasurement("projecttraiding.europe.news.processing.completed", "outcome=failed", 1);
        capture.AssertHasMeasurement("projecttraiding.europe.news.processing.duration", "outcome=failed");
        capture.AssertSum("projecttraiding.europe.news.processing.active", 0);

        NewsAgentOptions disabledOptions = new();
        ControlledChatClient disabledClient = new("OpenAI");
        EuropeTelegramRuntime disabledTelegram = new(new TelegramOptions(),
            NullLogger<EuropeTelegramRuntime>.Instance);
        using (disabledTelegram)
        using (NewsAgentExecutor disabledExecutor = new(disabledTelegram, disabledOptions,
            NullLogger<NewsAgentExecutor>.Instance, null!, configuration, disabledClient))
        {
            if (disabledExecutor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "disabled")))
                throw new InvalidOperationException("Выключенный контур принял публикацию.");
            capture.AssertMeasurement("projecttraiding.europe.news.queue.rejected", "reason=disabled", 1);
            capture.Listener.RecordObservableInstruments();
            capture.AssertLast("projecttraiding.europe.news.queue.capacity", 0);
        }
        if (disabledClient.CallCount != 0)
            throw new InvalidOperationException("Выключенный контур вызвал управляемого клиента.");

        NewsAgentOptions parallelOptions = new()
        {
            Enabled = true,
            QueueCapacity = 2,
            Workers = 2,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
        ControlledChatClient parallelClient = new("OpenAI");
        using IHost parallelHost = BuildManagedHost(configuration, parallelOptions, parallelClient);
        await parallelHost.StartAsync();
        NewsAgentExecutor parallelExecutor = parallelHost.Services.GetRequiredService<NewsAgentExecutor>();
        if (!parallelExecutor.TryEnqueuePublication(CreateSyntheticPublication("Telegram", "parallel metrics telegram"))
            || !parallelExecutor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "parallel metrics bigdata")))
            throw new InvalidOperationException("Параллельная очередь не приняла обе публикации.");
        parallelExecutor.UpdatePromptSnapshot(CreateSnapshot("parallel-metrics", "parallel-metrics",
            "Parallel metrics prompt", ["P"], 1));
        ControlledChatClient.CapturedCall firstParallelCall = await parallelClient.NextCallAsync();
        await parallelClient.NextCallAsync();
        if (parallelClient.CallCount != 2)
            throw new InvalidOperationException("Оба вызова не стартовали до освобождения любого из них.");
        capture.AssertObserved("projecttraiding.europe.news.processing.active", "source=Telegram", 1, 2);
        capture.AssertObserved("projecttraiding.europe.news.processing.active", "source=Bigdata", 1, 1);
        parallelClient.Release(firstParallelCall.Number);
        await Task.Delay(50);
        await parallelHost.StopAsync(TimeSpan.FromSeconds(2));
        capture.AssertSum("projecttraiding.europe.news.processing.active", 0);
        capture.AssertHasMeasurement("projecttraiding.europe.news.processing.completed", "outcome=cancelled");

        NewsAgentOptions timeoutOptions = new()
        {
            Enabled = true,
            QueueCapacity = 1,
            Workers = 1,
            RequestTimeout = TimeSpan.FromMilliseconds(100)
        };
        ControlledChatClient timeoutClient = new("OpenAI");
        using IHost timeoutHost = BuildManagedHost(configuration, timeoutOptions, timeoutClient);
        await timeoutHost.StartAsync();
        NewsAgentExecutor timeoutExecutor = timeoutHost.Services.GetRequiredService<NewsAgentExecutor>();
        timeoutExecutor.UpdatePromptSnapshot(CreateSnapshot("timeout-metrics", "timeout-metrics",
            "Timeout metrics prompt", ["T"], 1));
        if (!timeoutExecutor.TryEnqueuePublication(CreateSyntheticPublication("other-source", "timeout")))
            throw new InvalidOperationException("Очередь отклонила публикацию для проверки таймаута.");
        await timeoutClient.NextCallAsync();
        await capture.WaitForMeasurementAsync("projecttraiding.europe.news.processing.completed", "outcome=timeout");
        await timeoutHost.StopAsync(TimeSpan.FromSeconds(2));
        capture.AssertMeasurement("projecttraiding.europe.news.processing.completed", "outcome=timeout", 1);
        capture.AssertHasMeasurement("projecttraiding.europe.news.processing.duration", "outcome=timeout");
        capture.AssertSum("projecttraiding.europe.news.processing.active", 0);
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly ConcurrentQueue<RecordedMeasurement> _measurements = new();
        internal MeterListener Listener { get; } = new();

        internal MetricsCapture()
        {
            Listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == NewsAgentExecutor.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            Listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
                Add(instrument, value, tags));
            Listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Add(instrument, value, tags));
            Listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Add(instrument, value, tags));
            Listener.Start();
        }

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : IFormattable
        {
            string tagText = string.Join(",", tags.ToArray().Select(static tag =>
                $"{tag.Key}={tag.Value}"));
            _measurements.Enqueue(new RecordedMeasurement(instrument.Name,
                double.Parse(value.ToString(null, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture), tagText));
        }

        internal void AssertCount(string name, double expected) => AssertMeasurement(name, "", expected);

        internal void AssertLast(string name, double expected)
        {
            RecordedMeasurement? measurement = _measurements.LastOrDefault(item => item.Name == name);
            if (measurement is null || measurement.Value != expected)
                throw new InvalidOperationException($"Метрика {name} должна иметь значение {expected}.");
        }

        internal void AssertMeasurement(string name, string tagFragment, double expected)
        {
            RecordedMeasurement[] matches = _measurements.Where(item => item.Name == name
                && item.Tags.Contains(tagFragment, StringComparison.Ordinal)).ToArray();
            double actual = matches.Sum(static item => item.Value);
            if (actual != expected)
                throw new InvalidOperationException($"Метрика {name} с метками {tagFragment} имеет {actual}, ожидалось {expected}.");
        }

        internal void AssertHasMeasurement(string name, string tagFragment)
        {
            if (!_measurements.Any(item => item.Name == name
                && item.Tags.Contains(tagFragment, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Не записана метрика {name} с метками {tagFragment}.");
        }

        internal void AssertObserved(string name, string tagFragment, double value, int expectedCount)
        {
            int actual = _measurements.Count(item => item.Name == name
                && item.Tags.Contains(tagFragment, StringComparison.Ordinal) && item.Value == value);
            if (actual != expectedCount)
                throw new InvalidOperationException($"Для {name} с {tagFragment} найдено {actual} измерений со значением {value}, ожидалось {expectedCount}.");
        }

        internal async Task WaitForMeasurementAsync(string name, string tagFragment)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (_measurements.Any(item => item.Name == name
                    && item.Tags.Contains(tagFragment, StringComparison.Ordinal)))
                    return;
                await Task.Delay(10);
            }
            throw new InvalidOperationException($"Не дождались измерения {name} с метками {tagFragment}.");
        }

        internal void AssertSum(string name, double expected)
        {
            double actual = _measurements.Where(item => item.Name == name).Sum(static item => item.Value);
            if (actual != expected)
                throw new InvalidOperationException($"Сумма измерений {name} равна {actual}, ожидалось {expected}.");
        }

        public void Dispose() => Listener.Dispose();

        private sealed record RecordedMeasurement(string Name, double Value, string Tags);
    }

    private static async Task RunWorkerGateChecksAsync()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["News:Enabled"] = "true",
                ["News:QueueCapacity"] = "1",
                ["News:Workers"] = "1",
                ["News:RequestTimeout"] = "00:00:30",
                ["OpenAI:ApiKey"] = "local-test-key",
                ["LLM:ActiveProvider"] = "OpenAI",
                ["OpenAI:ActiveProfile"] = "default",
                ["OpenAI:Profiles:default:Model"] = "gpt-6-luna",
                ["OpenAI:Profiles:alternate:Model"] = "gpt-4.1-2025-04-14"
            }).Build();
        NewsAgentOptions options = NewsAgentOptions.Load(configuration);
        ControlledChatClient client = new("OpenAI");
        using IHost host = BuildManagedHost(configuration, options, client);
        await host.StartAsync();
        NewsAgentExecutor executor = host.Services.GetRequiredService<NewsAgentExecutor>();
        using (JsonDocument metadata = JsonDocument.Parse("{\"marker\":\"local\"}"))
        {
            AssertInvalidInput(executor, new NewsPublicationInput(" ", "text", null, DateTimeOffset.UnixEpoch,
                metadata.RootElement));
            AssertInvalidInput(executor, new NewsPublicationInput("Bigdata", " \t", null, DateTimeOffset.UnixEpoch,
                metadata.RootElement));
            using JsonDocument array = JsonDocument.Parse("[]");
            AssertInvalidInput(executor, new NewsPublicationInput("Bigdata", "text", null, DateTimeOffset.UnixEpoch,
                array.RootElement));
            AssertInvalidInput(executor, new NewsPublicationInput("Bigdata", "text", null,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)), metadata.RootElement));
            AssertInvalidInput(executor, new NewsPublicationInput("Bigdata", "text",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2)), DateTimeOffset.UnixEpoch,
                metadata.RootElement));
        }
        bool acceptedBeforeSnapshot;
        using (JsonDocument queuedMetadata = JsonDocument.Parse("{\"marker\":\"owned\"}"))
        {
            NewsPublicationInput queued = new("Telegram", "queued", null, DateTimeOffset.UnixEpoch,
                queuedMetadata.RootElement);
            acceptedBeforeSnapshot = executor.TryEnqueuePublication(queued);
        }
        if (!acceptedBeforeSnapshot
            || executor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "overflow")))
            throw new InvalidOperationException("Ограниченная очередь не отклонила вторую публикацию без снимка.");
        await Task.Delay(50);
        if (client.CallCount != 0)
            throw new InvalidOperationException("Очередь вызвала клиента до первого снимка.");
        if (executor.UpdatePromptSnapshot(CreateSnapshot("worker", "worker", "Worker prompt", ["W"], 1))
            != NewsSnapshotApplyStatus.Applied)
            throw new InvalidOperationException("Управляемый хост не применил первый снимок.");
        ControlledChatClient.CapturedCall call = await client.NextCallAsync();
        AssertCall(call, "OpenAI", "gpt-6-luna", "Worker prompt", ["W"], null);
        using (JsonDocument captured = JsonDocument.Parse(call.UserInput))
        {
            JsonElement publication = captured.RootElement.GetProperty("publication");
            if (publication.GetProperty("raw_text").GetString() != "queued"
                || publication.GetProperty("source_metadata").GetProperty("marker").GetString() != "owned")
                throw new InvalidOperationException("Метаданные очереди зависели от освобождённого JsonDocument.");
        }
        string firstAgentVersion = executor.CurrentExecutionSnapshot!.AgentVersion;
        LlmModelSelectionResult selection = executor.ActivateModel("OpenAI", "gpt-4.1-2025-04-14");
        if (selection.Failure != LlmModelSelectionFailure.None
            || executor.UpdatePromptSnapshot(CreateSnapshot("worker-2", "worker-2", "Updated worker prompt", ["W", "X"], 2))
                != NewsSnapshotApplyStatus.Applied
            || executor.CurrentExecutionSnapshot!.AgentVersion == firstAgentVersion)
            throw new InvalidOperationException("Смена модели и снимка не обновила атомарную конфигурацию.");
        client.Release(call.Number);
        if (!executor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "second")))
            throw new InvalidOperationException("Общая очередь отклонила публикацию после освобождения места.");
        ControlledChatClient.CapturedCall updatedCall = await client.NextCallAsync();
        AssertCall(updatedCall, "OpenAI", "gpt-4.1-2025-04-14", "Updated worker prompt", ["W", "X"], null);
        client.Release(updatedCall.Number);
        await Task.Delay(50);
        if (client.CallCount != 2)
            throw new InvalidOperationException("Переполнение очереди изменило число обработчиков.");
        await host.StopAsync(TimeSpan.FromSeconds(2));

        ControlledChatClient cancelledClient = new("OpenAI");
        using IHost waitingHost = BuildManagedHost(configuration, options, cancelledClient);
        await waitingHost.StartAsync();
        NewsAgentExecutor waitingExecutor = waitingHost.Services.GetRequiredService<NewsAgentExecutor>();
        waitingExecutor.TryEnqueuePublication(CreateSyntheticPublication("Telegram", "waiting"));
        await waitingHost.StopAsync(TimeSpan.FromSeconds(2));
        if (cancelledClient.CallCount != 0
            || waitingExecutor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "after close")))
            throw new InvalidOperationException("Отмена до первого снимка вызвала клиента.");

        await RunParallelPublicationChecksAsync(configuration);
        await RunBusyStopChecksAsync(configuration);
    }

    private static async Task RunParallelPublicationChecksAsync(IConfigurationRoot configuration)
    {
        NewsAgentOptions options = new()
        {
            Enabled = true,
            QueueCapacity = 2,
            Workers = 2,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
        ControlledChatClient client = new("OpenAI");
        using IHost host = BuildManagedHost(configuration, options, client);
        await host.StartAsync();
        NewsAgentExecutor executor = host.Services.GetRequiredService<NewsAgentExecutor>();
        NewsPublicationInput telegram = CreateSyntheticPublication("Telegram", "parallel telegram");
        NewsPublicationInput bigdata = CreateSyntheticPublication("Bigdata", "parallel bigdata");
        if (!executor.TryEnqueuePublication(telegram) || !executor.TryEnqueuePublication(bigdata))
            throw new InvalidOperationException("Общая очередь не приняла две параллельные публикации.");
        executor.UpdatePromptSnapshot(CreateSnapshot("parallel", "parallel", "Parallel prompt", ["P"], 1));
        ControlledChatClient.CapturedCall first = await client.NextCallAsync();
        ControlledChatClient.CapturedCall second = await client.NextCallAsync();
        if (client.CallCount != 2)
            throw new InvalidOperationException("Два работника не начали вызовы LLM до освобождения первого.");
        bool firstTelegram = first.UserInput.Contains("parallel telegram", StringComparison.Ordinal);
        bool secondTelegram = second.UserInput.Contains("parallel telegram", StringComparison.Ordinal);
        AssertPublication(first, firstTelegram ? telegram : bigdata);
        AssertPublication(second, secondTelegram ? telegram : bigdata);
        if (firstTelegram == secondTelegram || first.Instructions != "Parallel prompt"
            || second.Instructions != "Parallel prompt")
            throw new InvalidOperationException("Параллельные вызовы смешали источники или снимок.");
        client.Release(second.Number);
        client.Release(first.Number);
        await host.StopAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task RunBusyStopChecksAsync(IConfigurationRoot configuration)
    {
        NewsAgentOptions options = new()
        {
            Enabled = true,
            QueueCapacity = 1,
            Workers = 1,
            RequestTimeout = TimeSpan.FromSeconds(30)
        };
        ControlledChatClient client = new("OpenAI");
        using IHost host = BuildManagedHost(configuration, options, client);
        await host.StartAsync();
        NewsAgentExecutor executor = host.Services.GetRequiredService<NewsAgentExecutor>();
        executor.UpdatePromptSnapshot(CreateSnapshot("busy", "busy", "Busy prompt", ["B"], 1));
        if (!executor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "busy")))
            throw new InvalidOperationException("Очередь не приняла публикацию для проверки остановки.");
        await client.NextCallAsync();
        await host.StopAsync(TimeSpan.FromSeconds(2));
        if (executor.TryEnqueuePublication(CreateSyntheticPublication("Bigdata", "after stop")))
            throw new InvalidOperationException("После остановки исполнителя публикация принята.");
    }

    private static IHost BuildManagedHost(IConfigurationRoot configuration, NewsAgentOptions options,
        ControlledChatClient chatClient)
    {
        TelegramOptions telegramOptions = new();
        EuropeTelegramRuntime telegram = new(telegramOptions, NullLogger<EuropeTelegramRuntime>.Instance);
        return new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(telegramOptions);
            services.AddSingleton(telegram);
            services.AddSingleton(options);
            services.AddSingleton<NewsAgentExecutor>(provider => new NewsAgentExecutor(telegram, options,
                NullLogger<NewsAgentExecutor>.Instance,
                provider.GetRequiredService<IHostApplicationLifetime>(), configuration, chatClient));
            services.AddHostedService(provider => provider.GetRequiredService<NewsAgentExecutor>());
            services.AddHostedService(provider => provider.GetRequiredService<EuropeTelegramRuntime>());
        }).Build();
    }

    private static NewsAgentPromptSnapshot CreateSnapshot(string version, string promptVersion, string prompt,
        ImmutableArray<string> secids, long revision)
    {
        using JsonDocument schema = JsonDocument.Parse(NewsAgentJsonContext.ContractSchema);
        ImmutableArray<NewsInstrumentContextDTO> instruments = secids
            .Select(secid => new NewsInstrumentContextDTO(secid, $"Synthetic {secid}", "equity", null))
            .ToImmutableArray();
        return new NewsAgentPromptSnapshot(version, promptVersion, prompt, "Realtime", ["other"], [], instruments,
            schema.RootElement.Clone(), 256, 0.2f, false, revision);
    }

    private static JsonDocument CreateReorderedSchema()
    {
        using JsonDocument schema = JsonDocument.Parse(NewsAgentJsonContext.ContractSchema);
        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in schema.RootElement.EnumerateObject())
            {
                if (property.NameEquals("type")) continue;
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
            writer.WritePropertyName("type");
            schema.RootElement.GetProperty("type").WriteTo(writer);
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(output.ToArray());
    }

    private static TelegramChannelPost CreateTelegramPublication() => new(1, null, "Synthetic", 1,
        "Local context capture check", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static NewsPublicationInput CreateSyntheticPublication(string source, string rawText)
    {
        using JsonDocument metadata = JsonDocument.Parse("{\"marker\":\"local\"}");
        return new NewsPublicationInput(source, rawText, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            metadata.RootElement.Clone());
    }

    private static void AssertInvalidInput(NewsAgentExecutor executor, NewsPublicationInput publication)
    {
        try
        {
            executor.TryEnqueuePublication(publication);
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Некорректная публикация не была отклонена до записи в очередь.");
    }

    private static void AssertPublication(ControlledChatClient.CapturedCall call, NewsPublicationInput expected)
    {
        using JsonDocument input = JsonDocument.Parse(call.UserInput);
        JsonElement publication = input.RootElement.GetProperty("publication");
        bool sourceOk = publication.GetProperty("source").GetString() == expected.Source;
        bool textOk = publication.GetProperty("raw_text").GetString() == expected.RawText;
        bool publishedOk = publication.GetProperty("published_at").GetDateTimeOffset() == expected.PublishedAt;
        bool receivedOk = publication.GetProperty("received_at").GetDateTimeOffset() == expected.ReceivedAt;
        bool metadataOk = JsonElement.DeepEquals(publication.GetProperty("source_metadata"), expected.SourceMetadata);
        if (!sourceOk || !textOk || !publishedOk || !receivedOk || !metadataOk)
            throw new InvalidOperationException($"Общая схема запроса изменила поля публикации: {sourceOk}/{textOk}/{publishedOk}/{receivedOk}/{metadataOk}.");
    }

    private static void AssertCall(ControlledChatClient.CapturedCall call, string expectedProvider,
        string expectedModel, string expectedPrompt, ImmutableArray<string> expectedSecids,
        ReasoningEffort? expectedReasoningEffort)
    {
        if (call.Provider != expectedProvider || call.ModelId != expectedModel || call.Instructions != expectedPrompt)
            throw new InvalidOperationException("Вызов смешал провайдера, модель или промпт из разных снимков.");
        if (call.ReasoningEffort != expectedReasoningEffort)
            throw new InvalidOperationException("Вызов получил настройки reasoning другого профиля.");
        if (call.Temperature is not { } temperature || Math.Abs(temperature - 0.2f) > 0.0001f)
            throw new InvalidOperationException("Вызов не захватил ожидаемую настройку temperature.");

        using JsonDocument userInput = JsonDocument.Parse(call.UserInput);
        ImmutableArray<string> actualSecids = userInput.RootElement.GetProperty("instruments")
            .EnumerateArray()
            .Select(instrument => instrument.GetProperty("secid").GetString()!)
            .ToImmutableArray();
        if (!actualSecids.SequenceEqual(expectedSecids))
            throw new InvalidOperationException("Вызов не захватил ожидаемый полный набор инструментов.");
    }

    private static async Task ExpectControlledFailureAsync(Task request)
    {
        try
        {
            await request;
        }
        catch (InvalidOperationException error) when (error.Message == ControlledChatClient.ReleasedMessage)
        {
            return;
        }

        throw new InvalidOperationException("Управляемый локальный вызов завершился неожиданно.");
    }

    private sealed class ControlledChatClient : IChatClient
    {
        internal const string ReleasedMessage = "Локальный захват контекста завершён.";
        private readonly Channel<CapturedCall> _calls = Channel.CreateUnbounded<CapturedCall>();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _releases = new();
        private int _callCount;
        private int _disposeCount;

        internal ControlledChatClient(string provider) => this.Provider = provider;

        internal string Provider { get; }
        internal int CallCount => Volatile.Read(ref _callCount);
        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            int number = Interlocked.Increment(ref _callCount);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releases[number] = release;
            ChatMessage[] capturedMessages = messages.ToArray();
            if (capturedMessages.Length != 1 || capturedMessages[0].Role != ChatRole.User)
                throw new InvalidOperationException("Промпт должен передаваться один раз через Instructions.");
            await _calls.Writer.WriteAsync(new CapturedCall(number, Provider, options?.ModelId, options?.Temperature,
                options?.Reasoning?.Effort, options?.Instructions, capturedMessages[0].Text), cancellationToken);
            await release.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException(ReleasedMessage);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Потоковый вызов не используется локальной диагностикой.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        internal async Task<CapturedCall> NextCallAsync() => await _calls.Reader.ReadAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        internal void Release(int number)
        {
            if (_releases.TryGetValue(number, out TaskCompletionSource? release)) release.TrySetResult();
        }

        internal void ReleaseAll()
        {
            foreach (TaskCompletionSource release in _releases.Values) release.TrySetResult();
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            _calls.Writer.TryComplete();
        }

        internal sealed record CapturedCall(int Number, string Provider, string? ModelId, float? Temperature,
            ReasoningEffort? ReasoningEffort, string? Instructions, string UserInput);
    }
}
