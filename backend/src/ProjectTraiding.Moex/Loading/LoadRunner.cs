using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    public enum LoadStatus { NotFound, NotClaimed, Done }

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

        public LoadRunner(
            MoexLoadTaskReader taskReader,
            MoexLoadTaskWriter taskWriter,
            MoexLoadedRangeWriter rangeWriter,
            LoadHandlerDispatcher dispatcher)
        {
            _taskReader = taskReader;
            _taskWriter = taskWriter;
            _rangeWriter = rangeWriter;
            _dispatcher = dispatcher;
        }

        public async Task<LoadOutcome> RunAsync(Guid taskId, CancellationToken ct, bool alreadyClaimed = false)
        {
            MoexLoadTask? task = await _taskReader.GetByIdAsync(taskId, ct);
            if (task is null)
                return new LoadOutcome(LoadStatus.NotFound, 0);

            if (task.StorageTarget != "clickhouse")
                throw new InvalidOperationException(
                    $"Задача {taskId} не нацелена на ClickHouse (storage_target={task.StorageTarget}).");

            ILoadHandler? handler = _dispatcher.Resolve(task);
            if (handler is null)
                throw new InvalidOperationException(
                    $"Нет обработчика для задачи {taskId} (data_kind={task.DataKind}, market={task.Market}, interval={task.CandleInterval}).");

            // Фоновый подбор уже перевёл задачу в running одним атомарным запросом — повторный
            // claim не нужен. Ручной запуск через операторскую точку приходит без захвата.
            if (!alreadyClaimed)
            {
                bool claimed = await _taskWriter.MarkRunningAsync(taskId, ct);
                if (!claimed)
                    return new LoadOutcome(LoadStatus.NotClaimed, 0);
            }

            try
            {
                LoadStopOutcome stopOutcome = new LoadStopOutcome();

                CandlesWriteSummary summary = await handler.LoadAsync(task, stopOutcome, ct);

                // Настоящая причина из потока; пустой держатель трактуем как штатное исчерпание.
                string stopReason = stopOutcome.StopReason ?? "range_exhausted";

                // Журнал результата пишем всегда — диапазон покрыт настолько, насколько прочитан.
                await _rangeWriter.UpsertAsync(task, summary.RowsRead, summary.LastToken, ct);

                if (stopOutcome.IsPartial)
                    await _taskWriter.MarkPartialAsync(taskId, summary.RowsRead, stopReason, summary.LastToken, ct);
                else
                    await _taskWriter.MarkDoneAsync(taskId, summary.RowsRead, stopReason, summary.LastToken, ct);

                return new LoadOutcome(LoadStatus.Done, summary.RowsRead);
            }
            catch (OperationCanceledException)
            {
                await _taskWriter.MarkErrorAsync(taskId, "cancelled", "cancelled", CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                await _taskWriter.MarkErrorAsync(taskId, ex.Message, null, CancellationToken.None);
                throw;
            }
        }
    }
}
