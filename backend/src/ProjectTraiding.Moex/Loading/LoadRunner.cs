using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    public enum LoadStatus { NotFound, NotClaimed, Done, Failed }

    public readonly record struct LoadOutcome(LoadStatus Status, long RowsCovered);

    /// <summary>
    /// Координатор исторической загрузки одной задачи любого вида: читает задачу, выбирает
    /// обработчик вида через диспетчер, берёт задачу в работу, гонит данные в ClickHouse, по
    /// успеху пишет результат и закрывает задачу, по сбою помечает отказ. Вид, рынок и интервал
    /// различает диспетчер; координатор знает только жизненный цикл задачи.
    /// </summary>
    public sealed class LoadRunner
    {
        private readonly MoexLoadTaskReader _taskReader;
        private readonly MoexLoadTaskWriter _taskWriter;
        private readonly MoexLoadedRangeWriter _rangeWriter;
        private readonly LoadHandlerDispatcher _dispatcher;
        private readonly ILoadProgressReporter _progress;
        private readonly ProjectTraiding.Moex.StorageBase.Redis.LoadedRangeEventPublisher _rangeEventPublisher;
        private readonly ILogger<LoadRunner> _logger;

        public LoadRunner(
            MoexLoadTaskReader taskReader,
            MoexLoadTaskWriter taskWriter,
            MoexLoadedRangeWriter rangeWriter,
            LoadHandlerDispatcher dispatcher,
            ILoadProgressReporter progress,
            ProjectTraiding.Moex.StorageBase.Redis.LoadedRangeEventPublisher rangeEventPublisher,
            ILogger<LoadRunner> logger)
        {
            _taskReader = taskReader;
            _taskWriter = taskWriter;
            _rangeWriter = rangeWriter;
            _dispatcher = dispatcher;
            _progress = progress;
            _rangeEventPublisher = rangeEventPublisher;
            _logger = logger;
        }

        public async Task<LoadOutcome> RunAsync(Guid taskId, CancellationToken ct, bool alreadyClaimed = false)
        {
            bool ownsRunningTask = alreadyClaimed;

            // Замер начинается с момента владения задачей, а не с входа в метод. На фоновом пути
            // задача захвачена подбором ещё до вызова, поэтому отсчёт идёт сразу. На ручном пути
            // владение наступает только после успешного перевода в рабочее состояние, и чтение
            // строки с попыткой захвата в длительность задания не входят.
            long taskStart = Stopwatch.GetTimestamp();

            Activity? taskActivity = null;

            bool metadataResolved = false;
            bool activeCounted = false;
            bool completionRecorded = false;

            string dataKind = string.Empty;
            string market = string.Empty;
            string outcome = MoexOutcomes.Error;

            bool summaryAvailable = false;
            long rowsForTelemetry = 0;
            string? stopReasonForTelemetry = null;
            string? errorTypeForTelemetry = null;

            try
            {
                MoexLoadTask? task = await _taskReader.GetByIdAsync(taskId, ct);
                if (task is null)
                    return new LoadOutcome(LoadStatus.NotFound, 0);

                // Фоновый подбор уже перевёл задачу в running одним атомарным запросом — повторный
                // claim не нужен. Ручной запуск через операторскую точку приходит без захвата.
                if (!alreadyClaimed)
                {
                    bool claimed = await _taskWriter.MarkRunningAsync(taskId, ct);
                    if (!claimed)
                        return new LoadOutcome(LoadStatus.NotClaimed, 0);

                    ownsRunningTask = true;

                    // Ручной путь: владение получено только сейчас — отсчёт начинается заново.
                    taskStart = Stopwatch.GetTimestamp();
                }

                dataKind = MoexDataKinds.FromTaskDataKind(task.DataKind);
                market = task.Market;
                metadataResolved = true;

                // Создание корня и начальные атрибуты защищены независимо: сбой поставщика
                // телеметрии не имеет права отменить загрузку или помешать другим каналам.
                try
                {
                    taskActivity = MoexTelemetry.ActivitySource.StartActivity("moex.history.task");
                }
                catch
                {
                }

                try
                {
                    taskActivity?.SetTag(MoexTelemetryAttributes.TaskId, taskId);
                    taskActivity?.SetTag(MoexTelemetryAttributes.DataKind, dataKind);
                    taskActivity?.SetTag(MoexTelemetryAttributes.Market, market);
                    taskActivity?.SetTag(MoexTelemetryAttributes.Secid, task.Secid);
                }
                catch
                {
                }

                try
                {
                    MoexMetrics.LoadTasksActive.Add(
                        1,
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, dataKind),
                        new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, market));
                    activeCounted = true;
                }
                catch
                {
                    // Признак остаётся ложным: уменьшать то, что не увеличилось, нельзя.
                }

                if (task.StorageTarget != "clickhouse")
                    throw new InvalidOperationException(
                        $"Задача {taskId} не нацелена на ClickHouse (storage_target={task.StorageTarget}).");

                ILoadHandler? handler = _dispatcher.Resolve(task);
                if (handler is null)
                    throw new InvalidOperationException(
                        $"Нет обработчика для задачи {taskId} (data_kind={task.DataKind}, market={task.Market}, interval={task.CandleInterval}).");

                LoadStopOutcome stopOutcome = new LoadStopOutcome();

                RowWriteSummary summary = await handler.LoadAsync(task, stopOutcome, _progress, ct);
                summaryAvailable = true;
                rowsForTelemetry = summary.RowsRead;

                // Настоящая причина из потока; пустой держатель трактуем как штатное исчерпание.
                string stopReason = stopOutcome.StopReason ?? "range_exhausted";

                // Частичный исход = сработал защитный предел страниц: диапазон шире, чем можно
                // безопасно вычитать за один проход. Покрытие НЕ пишем (иначе неполный диапазон
                // закрепился бы как полный) и закрываем задачу отказом с машинной причиной.
                // Оператор пересоздаёт задачи меньшим окном. Ветвление стоит ДО записи покрытия —
                // в этом суть правки А1.
                if (stopOutcome.IsPartial)
                {
                    outcome = MoexOutcomes.Error;
                    stopReasonForTelemetry = stopReason;

                    await _taskWriter.MarkErrorAsync(
                        taskId,
                        "диапазон превышает предел страниц: пересоздайте задачи с меньшим окном",
                        stopReason,
                        ct);
                    return new LoadOutcome(LoadStatus.Failed, summary.RowsRead);
                }

                outcome = MoexOutcomes.Success;
                stopReasonForTelemetry = stopReason;

                // Штатное полное покрытие: журнал результата, извещение витрины, закрытие успехом.
                await _rangeWriter.UpsertAsync(task, summary.RowsRead, summary.LastToken, ct);
                await _rangeEventPublisher.PublishChangedAsync(task.Secid);
                await _taskWriter.MarkDoneAsync(taskId, summary.RowsRead, stopReason, summary.LastToken, ct);

                return new LoadOutcome(LoadStatus.Done, summary.RowsRead);
            }
            catch (OperationCanceledException)
            {
                outcome = MoexOutcomes.Cancelled;
                stopReasonForTelemetry = null;
                errorTypeForTelemetry = null;

                // Остановка хоста, а не сбой задачи: возвращаем в очередь, а не в error.
                // Иначе задача стала бы сиротой — автоподбор error не берёт (правка А2).
                // CancellationToken.None: ct уже отменён, но статус закрыть обязаны.
                if (ownsRunningTask)
                {
                    try
                    {
                        await _taskWriter.RequeueAfterCancelAsync(taskId, CancellationToken.None);
                    }
                    catch (Exception requeueException)
                    {
                        // Если возврат в очередь отказал, задача осталась running: это отказ,
                        // а не благополучная отмена. Наружу выходит именно ошибка команды базы.
                        outcome = MoexOutcomes.Error;
                        errorTypeForTelemetry = MoexMetrics.ClassifyError(requeueException);
                        throw;
                    }
                }

                throw;
            }
            catch (Exception ex)
            {
                outcome = MoexOutcomes.Error;
                stopReasonForTelemetry = null;
                errorTypeForTelemetry = MoexMetrics.ClassifyError(ex);

                if (ownsRunningTask)
                    await _taskWriter.MarkErrorAsync(taskId, ex.Message, null, CancellationToken.None);

                throw;
            }
            finally
            {
                if (metadataResolved && !completionRecorded)
                {
                    completionRecorded = true;
                    CompleteTask(
                        taskId,
                        dataKind,
                        market,
                        outcome,
                        summaryAvailable ? rowsForTelemetry : 0,
                        taskStart,
                        stopReasonForTelemetry,
                        errorTypeForTelemetry,
                        taskActivity);
                }

                try
                {
                    taskActivity?.Dispose();
                }
                catch
                {
                    // Завершение отрезка вызывает обработчики поставщика трасс и может отказать.
                }

                if (activeCounted)
                {
                    try
                    {
                        MoexMetrics.LoadTasksActive.Add(
                            -1,
                            new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, dataKind),
                            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, market));
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Записывает итог задания по трём каналам. Каждый канал защищён отдельно:
        /// метод вызывается из блока освобождения, и вышедшее отсюда исключение подменило бы
        /// исходную ошибку загрузки, а успешное задание превратило бы в отказ.
        ///
        /// Порядок каналов выбран по вероятности отказа: счётчики и отрезок практически
        /// не бросают, тогда как запись в журнал уходит во внешнего поставщика и отказать
        /// может. Поэтому журнал идёт последним — его сбой не лишает нас двух других каналов.
        ///
        /// Проглатывание здесь молчаливое, и это осознанное исключение из общего запрета:
        /// сообщить о сбое телеметрии можно было бы только через телеметрию, а она в этот
        /// момент и отказала. Единственная альтернатива — уронить задание из-за неудачной
        /// записи наблюдения, что заведомо хуже.
        /// </summary>
        private void CompleteTask(
            Guid taskId,
            string dataKind,
            string market,
            string outcome,
            long rows,
            long startTimestamp,
            string? stopReason,
            string? errorType,
            Activity? taskActivity)
        {
            TimeSpan duration = Stopwatch.GetElapsedTime(startTimestamp);

            // 1. Метрики задания — прежние, разрез и значения не меняются.
            try
            {
                MoexMetrics.LoadTasksCompleted.Add(
                    1,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, dataKind),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, market),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Outcome, outcome));

                MoexMetrics.LoadTaskDuration.Record(
                    duration.TotalSeconds,
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, dataKind),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, market),
                    new KeyValuePair<string, object?>(MoexTelemetryAttributes.Outcome, outcome));
            }
            catch
            {
            }

            // 2. Итоговые атрибуты и состояние корня — до журнала.
            try
            {
                taskActivity?.SetTag(MoexTelemetryAttributes.Outcome, outcome);
                if (stopReason is not null)
                    taskActivity?.SetTag(MoexTelemetryAttributes.StopReason, stopReason);
                if (errorType is not null)
                    taskActivity?.SetTag(MoexTelemetryAttributes.ErrorType, errorType);

                taskActivity?.SetStatus(
                    outcome == MoexOutcomes.Error
                        ? ActivityStatusCode.Error
                        : ActivityStatusCode.Ok);
            }
            catch
            {
            }

            // 3. Итоговое событие журнала — последним.
            try
            {
                MoexLoadTaskLogMessages.TaskCompleted(
                    _logger,
                    outcome == MoexOutcomes.Error ? LogLevel.Error : LogLevel.Information,
                    taskId,
                    dataKind,
                    market,
                    outcome,
                    rows,
                    duration,
                    stopReason,
                    errorType);
            }
            catch
            {
            }
        }
    }
}
