using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    public enum CandlesLoadStatus { NotFound, NotClaimed, Done }

    public readonly record struct CandlesLoadOutcome(CandlesLoadStatus Status, long RowsCovered);

    /// <summary>
    /// Координатор исторической загрузки свечей одной задачи: читает задачу, берёт в работу,
    /// гонит свечи в ClickHouse пачками, по успеху пишет результат и закрывает задачу,
    /// по сбою помечает отказ. Единственное место, знающее клиента, писатель ClickHouse
    /// и журнал задач вместе.
    /// </summary>
    public sealed class CandlesLoadRunner
    {
        private readonly MoexLoadTaskReader _taskReader;
        private readonly MoexLoadTaskWriter _taskWriter;
        private readonly MoexHttpAlgClient _algClient;
        private readonly CandlesWriter _candlesWriter;
        private readonly MoexLoadedRangeWriter _rangeWriter;

        public CandlesLoadRunner(
            MoexLoadTaskReader taskReader,
            MoexLoadTaskWriter taskWriter,
            MoexHttpAlgClient algClient,
            CandlesWriter candlesWriter,
            MoexLoadedRangeWriter rangeWriter)
        {
            _taskReader = taskReader;
            _taskWriter = taskWriter;
            _algClient = algClient;
            _candlesWriter = candlesWriter;
            _rangeWriter = rangeWriter;
        }

        public async Task<CandlesLoadOutcome> RunAsync(Guid taskId, CancellationToken ct)
        {
            MoexLoadTask? task = await _taskReader.GetByIdAsync(taskId, ct);
            if (task is null)
                return new CandlesLoadOutcome(CandlesLoadStatus.NotFound, 0);

            if (task.DataKind != "candles" || task.CandleInterval is null)
                throw new InvalidOperationException(
                    $"Задача {taskId} не является свечной (data_kind={task.DataKind}, interval={task.CandleInterval}).");
            if (task.StorageTarget != "clickhouse")
                throw new InvalidOperationException(
                    $"Задача {taskId} не нацелена на ClickHouse (storage_target={task.StorageTarget}).");

            bool claimed = await _taskWriter.MarkRunningAsync(taskId, ct);
            if (!claimed)
                return new CandlesLoadOutcome(CandlesLoadStatus.NotClaimed, 0);

            try
            {
                string method = BuildCandlesMethod(task);
                Dictionary<string, string> query = BuildCandlesQuery(task);
                string captureMarket = task.Market == "stock"
                    ? RawCaptureMarkets.Stock
                    : RawCaptureMarkets.Futures;

                IAsyncEnumerable<List<CandlesDTO>> pages = _algClient.GetCandles(
                    method, query, captureMarket: captureMarket, secid: task.Secid, cancellationToken: ct);

                CandlesWriteSummary summary = await _candlesWriter.WriteRangeAsync(
                    task.Secid, task.SourceContractVersion, task.WriterVersion, pages, ct);

                // В учёт диапазона идёт покрытый объём (RowsRead), а не отчёт драйвера.
                await _rangeWriter.UpsertAsync(task, summary.RowsRead, summary.LastToken, ct);
                await _taskWriter.MarkDoneAsync(taskId, summary.RowsRead, "range_exhausted", summary.LastToken, ct);

                return new CandlesLoadOutcome(CandlesLoadStatus.Done, summary.RowsRead);
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

        // Доска в адресе в РАЗНОМ регистре: у акций строчными (boards/tqbr),
        // у фьючерсов прописными (boards/RFUD) — форма, проверенная диагностикой.
        private static string BuildCandlesMethod(MoexLoadTask task)
        {
            if (task.Market == "stock")
            {
                string board = task.Boardid.ToLowerInvariant();
                return $"/engines/stock/markets/shares/boards/{board}/securities/{task.Secid}/candles.json";
            }
            else
            {
                string board = task.Boardid.ToUpperInvariant();
                return $"/engines/futures/markets/forts/boards/{board}/securities/{task.Secid}/candles.json";
            }
        }

        private static Dictionary<string, string> BuildCandlesQuery(MoexLoadTask task)
        {
            return new Dictionary<string, string>
            {
                ["interval"] = task.CandleInterval!.Value.ToString(CultureInfo.InvariantCulture),
                ["from"] = task.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["till"] = task.DateTill.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
        }
    }
}
