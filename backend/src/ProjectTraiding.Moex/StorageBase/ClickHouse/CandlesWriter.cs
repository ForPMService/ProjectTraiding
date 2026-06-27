using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Писатель минутных свечей в ClickHouse: режет поток страниц на пачки фиксированного
    /// размера и отдаёт каждую исполнителю одним INSERT с токеном уровня пачки.
    /// Источник потока безразличен — биржу писатель не вызывает.
    /// v1 — full-range retry: при сбое повторяется весь диапазон, записанные пачки
    /// ClickHouse отсекает по токенам. Возобновления с середины нет.
    /// </summary>
    public sealed class CandlesWriter
    {
        private readonly ClickHouseInsertExecutor _executor;
        private readonly ILogger<CandlesWriter> _logger;
        private readonly int _batchSize;

        public CandlesWriter(ClickHouseInsertExecutor executor, ILogger<CandlesWriter> logger, int batchSize)
        {
            if (batchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(batchSize), "Размер пачки должен быть положительным.");

            _executor = executor;
            _logger = logger;
            _batchSize = batchSize;
        }

        /// <summary>
        /// Пишет весь поток свечей одного диапазона пачками по _batchSize строк.
        /// Возвращает покрытый объём (RowsRead), отчёт драйвера (RowsInsertedReported) и токен
        /// последней пачки. Нарезка детерминирована (сквозной счёт от начала диапазона).
        /// </summary>
        public async Task<CandlesWriteSummary> WriteRangeAsync(
            string secid,
            string sourceContractVersion,
            string writerVersion,
            IAsyncEnumerable<List<CandlesDTO>> pages,
            CancellationToken ct)
        {
            CandlesRowMap.EnsureSecid(secid);

            List<object?[]> batch = new List<object?[]>(_batchSize);
            DateTime batchFirstBegin = default;
            DateTime batchLastBegin = default;
            long rowsRead = 0;
            long rowsInsertedReported = 0;
            string? lastToken = null;

            await foreach (List<CandlesDTO> page in pages.WithCancellation(ct))
            {
                foreach (CandlesDTO candle in page)
                {
                    object?[] row = CandlesRowMap.ToRow(candle, secid);
                    DateTime begin = candle.Begin!.Value;

                    if (batch.Count == 0)
                        batchFirstBegin = begin;
                    batchLastBegin = begin;

                    batch.Add(row);
                    rowsRead++;

                    if (batch.Count >= _batchSize)
                    {
                        (lastToken, long reported) = await FlushAsync(
                            secid, sourceContractVersion, writerVersion,
                            batch, batchFirstBegin, batchLastBegin, ct);
                        rowsInsertedReported += reported;
                        batch.Clear();
                    }
                }
            }

            if (batch.Count > 0)
            {
                (lastToken, long reported) = await FlushAsync(
                    secid, sourceContractVersion, writerVersion,
                    batch, batchFirstBegin, batchLastBegin, ct);
                rowsInsertedReported += reported;
            }

            ClickHouseWriterLogMessages.RangeWritten(_logger, secid, rowsRead, rowsInsertedReported);
            return new CandlesWriteSummary(rowsRead, rowsInsertedReported, lastToken);
        }

        private async Task<(string Token, long Reported)> FlushAsync(
            string secid, string sourceContractVersion, string writerVersion,
            IReadOnlyList<object?[]> batch, DateTime firstBegin, DateTime lastBegin,
            CancellationToken ct)
        {
            string token = BuildToken(
                secid, firstBegin, lastBegin, batch.Count, sourceContractVersion, writerVersion);
            long reported = await _executor.InsertAsync(
                CandlesRowMap.Table, CandlesRowMap.Columns, CandlesRowMap.ColumnTypes, batch, token, ct);
            return (token, reported);
        }

        // candles:1m:{secid}:{begin первой}:{begin последней}:{row_count}:{версии}.
        // Инвариантный формат времени — границы пачки воспроизводимы при повторе диапазона.
        private static string BuildToken(
            string secid, DateTime firstBegin, DateTime lastBegin, int rowCount,
            string sourceContractVersion, string writerVersion)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"candles:1m:{secid}:{firstBegin:yyyy-MM-ddTHH:mm:ss.fff}:{lastBegin:yyyy-MM-ddTHH:mm:ss.fff}:{rowCount}:{sourceContractVersion}:{writerVersion}");
        }
    }
}
