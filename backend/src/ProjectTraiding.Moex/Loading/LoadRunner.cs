using ProjectTraiding.Moex.Clients;
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
    /// <summary>
    /// Координатор исторической загрузки одной задачи любого вида: читает задачу, выбирает
    /// обработчик вида через диспетчер, берёт задачу в работу, гонит данные в ClickHouse, по
    /// успеху пишет результат и закрывает задачу, по сбою помечает отказ. Вид, рынок и интервал
    /// различает диспетчер; координатор знает только жизненный цикл задачи.
    /// </summary>
    public sealed class LoadRunner
    {
        // Опрос раз в три секунды: команда оператора редкая, задание идёт минутами.
        private static readonly TimeSpan CancelPollInterval = TimeSpan.FromSeconds(3);

        private readonly MoexLoadTaskReader _taskReader;
        private readonly MoexLoadTaskWriter _taskWriter;
        private readonly MoexLoadedRangeWriter _rangeWriter;
        private readonly SpecLoadHandler _loader;
        private readonly ILogger<LoadRunner> _logger;

        public LoadRunner(
            MoexLoadTaskReader taskReader,
            MoexLoadTaskWriter taskWriter,
            MoexLoadedRangeWriter rangeWriter,
            SpecLoadHandler loader,
            ILogger<LoadRunner> logger)
        {
            _taskReader = taskReader;
            _taskWriter = taskWriter;
            _rangeWriter = rangeWriter;
            _loader = loader;
            _logger = logger;
        }

        /// <summary>
        /// Фоновый путь: задача пришла готовой строкой из самого захвата и уже находится
        /// в рабочем состоянии. Повторного чтения по идентификатору здесь нет и быть не должно.
        /// Отсчёт длительности идёт с момента входа — как и прежде на этом пути.
        ///
        /// Повторное чтение после захвата исчезает сознательно, и у этого есть граница.
        /// Прежде между захватом и чтением существовало окно: если строку задачи в этот
        /// промежуток удаляла очистка данных инструмента, чтение возвращало пусто и загрузка
        /// не начиналась вовсе. Теперь загрузка пойдёт по снимку, полученному самим захватом.
        ///
        /// Это та же граница, которую контур удаления уже описал и принял: признак начатого
        /// удаления отсекает подбор и ручной запуск, начатые после его фиксации, а команды,
        /// начатые раньше фиксации, не останавливает. За пределами этой границы поведение
        /// фонового пути прежнее.
        /// </summary>
        public async Task RunClaimedAsync(MoexLoadTask task, CancellationToken ct)
        {
            long taskStart = Stopwatch.GetTimestamp();
            Guid taskId = task.Id;

            Activity? taskActivity = null;

            bool metadataResolved = false;
            bool activeCounted = false;

            string dataKind = string.Empty;
            string market = string.Empty;
            string outcome = MoexOutcomes.Error;

            long rowsForTelemetry = 0;
            string? stopReasonForTelemetry = null;
            string? errorTypeForTelemetry = null;

            try
            {
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

                // PostgreSQL раньше ClickHouse: покрытие снимается до удаления данных.
                // Обратный порядок оставляет строку 'ok' на пустом диапазоне, если
                // источник откажет после удаления.
                await _rangeWriter.RewriteOverlappingHistoryAsync(task, ct);

                LoadStopOutcome stopOutcome = new LoadStopOutcome();

                using CancellationTokenSource operatorCts = new CancellationTokenSource();
                using CancellationTokenSource linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(ct, operatorCts.Token);
                using CancellationTokenSource watcherStopCts = new CancellationTokenSource();

                Task watcherTask = WatchCancellationAsync(taskId, operatorCts, watcherStopCts.Token);

                RowWriteSummary summary;
                try
                {
                    summary = await _loader.LoadAsync(task, stopOutcome, linkedCts.Token);
                }
                finally
                {
                    watcherStopCts.Cancel();
                    await watcherTask;
                }

                // Последняя проверка перед финализацией. Локального токена мало: спутник мог спать
                // свои три секунды, пока обработчик заканчивал, и уже записанный запрос остался бы
                // незамеченным — задание получило бы done и полное покрытие вопреки команде.
                if (await _taskReader.IsCancelRequestedAsync(taskId, CancellationToken.None))
                    operatorCts.Cancel();

                linkedCts.Token.ThrowIfCancellationRequested();

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
                    return;
                }

                outcome = MoexOutcomes.Success;
                stopReasonForTelemetry = stopReason;

                // Штатное полное покрытие: журнал результата и закрытие успехом.
                await _rangeWriter.UpsertAsync(
                    task, summary.RowsRead, summary.RowsSkipped, CancellationToken.None);
                await _taskWriter.MarkDoneAsync(taskId, summary.RowsRead, stopReason, CancellationToken.None);

                return;
            }
            catch (OperationCanceledException)
            {
                outcome = MoexOutcomes.Cancelled;
                errorTypeForTelemetry = null;

                string? finalStatus;
                try
                {
                    finalStatus = await _taskWriter.FinalizeCancellationAsync(taskId, CancellationToken.None);
                }
                catch (Exception closeException)
                {
                    outcome = MoexOutcomes.Error;
                    errorTypeForTelemetry = MoexMetrics.ClassifyError(closeException);
                    throw;
                }

                bool operatorCancelled = finalStatus == "cancelled";

                // Причина видна не только в базе, но и в трассировке: иначе операторская отмена
                // и внешняя отмена выполнения слились бы в один исход наблюдаемости.
                stopReasonForTelemetry = operatorCancelled ? "operator_cancelled" : null;

                // Операторская отмена — штатный исход. Пробрасывать исключение здесь ошибка: в фоновом
                // исполнителе оба фильтра проверяют токен остановки приложения, который при операторской
                // отмене не отменён, и отмена записалась бы в журнал как отказ загрузки.
                if (operatorCancelled)
                    return;

                throw;
            }
            catch (Exception ex)
            {
                outcome = MoexOutcomes.Error;
                stopReasonForTelemetry = null;
                errorTypeForTelemetry = MoexMetrics.ClassifyError(ex);

                await _taskWriter.MarkErrorAsync(taskId, ex.Message, null, CancellationToken.None);

                throw;
            }
            finally
            {
                if (metadataResolved)
                {
                    CompleteTask(
                        taskId,
                        dataKind,
                        market,
                        outcome,
                        rowsForTelemetry,
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
        /// Спутник одного выполняющегося задания: опрашивает признак операторского запроса
        /// и при его появлении отменяет операторский источник. Останавливается вместе
        /// с заданием через stopToken.
        /// </summary>
        private async Task WatchCancellationAsync(
            Guid taskId,
            CancellationTokenSource operatorCts,
            CancellationToken stopToken)
        {
            while (!stopToken.IsCancellationRequested)
            {
                try
                {
                    // Задержка первая: задание только что взято в работу, запрос появиться не успел.
                    await Task.Delay(CancelPollInterval, stopToken);

                    if (await _taskReader.IsCancelRequestedAsync(taskId, stopToken))
                    {
                        operatorCts.Cancel();
                        return;
                    }
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    return;   // штатная остановка спутника вместе с заданием
                }
                catch (Exception ex)
                {
                    // Сбой одного опроса не прекращает наблюдение и не имеет права уронить
                    // загрузку. После восстановления доступа следующий оборот увидит запрос.
                    MoexLoadTaskLogMessages.CancelWatchPollFailed(_logger, ex, taskId, ex.GetType().Name);
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
