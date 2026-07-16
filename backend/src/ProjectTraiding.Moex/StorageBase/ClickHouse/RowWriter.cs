using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Loading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Писатель рядов одного вида в ClickHouse: режет поток страниц на пачки фиксированного
    /// размера и отдаёт каждую исполнителю одним INSERT с токеном уровня пачки. Форму вставки
    /// (таблицу, столбцы, типы, превращение строки, метку времени, префикс токена) задаёт карта;
    /// писатель тип источника не знает. Источник потока безразличен — биржу писатель не вызывает.
    /// v1 — full-range retry: при сбое повторяется весь диапазон, записанные пачки ClickHouse
    /// отсекает по токенам. Возобновления с середины нет.
    /// </summary>
    public sealed class RowWriter<T>
    {
        private readonly ClickHouseInsertExecutor _executor;
        private readonly IRowMap<T> _map;
        private readonly ILogger<RowWriter<T>> _logger;
        private readonly int _batchSize;

        public RowWriter(
            ClickHouseInsertExecutor executor,
            IRowMap<T> map,
            ILogger<RowWriter<T>> logger,
            int batchSize)
        {
            if (batchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(batchSize), "Размер пачки должен быть положительным.");

            _executor = executor;
            _map = map;
            _logger = logger;
            _batchSize = batchSize;
        }

        /// <summary>
        /// Пишет весь поток строк одного диапазона пачками по _batchSize. Возвращает покрытый
        /// объём (RowsRead), отчёт драйвера (RowsInsertedReported) и токен последней пачки.
        /// Нарезка детерминирована (сквозной счёт от начала диапазона).
        /// </summary>
        public async Task<RowWriteSummary> WriteRangeAsync(
            Guid taskId,
            string secid,
            string sourceContractVersion,
            string writerVersion,
            IAsyncEnumerable<List<T>> pages,
            ILoadProgressReporter progress,
            CancellationToken ct)
        {
            _map.EnsureRangeValid(secid);

            List<object?[]> batch = new List<object?[]>(_batchSize);
            DateTime batchFirstTime = default;
            DateTime batchLastTime = default;
            long rowsRead = 0;
            long rowsInsertedReported = 0;
            string? lastToken = null;

            await foreach (List<T> page in pages.WithCancellation(ct))
            {
                foreach (T item in page)
                {
                    (object?[] row, DateTime time) = _map.ToRow(item, secid, null);

                    if (batch.Count == 0)
                        batchFirstTime = time;
                    batchLastTime = time;

                    batch.Add(row);
                    rowsRead++;

                    if (batch.Count >= _batchSize)
                    {
                        (lastToken, long reported) = await FlushAsync(
                            secid, sourceContractVersion, writerVersion,
                            batch, batchFirstTime, batchLastTime, ct);
                        rowsInsertedReported += reported;
                        batch.Clear();

                        await progress.ReportAsync(taskId, rowsRead, batchLastTime, ct);
                    }
                }
            }

            if (batch.Count > 0)
            {
                (lastToken, long reported) = await FlushAsync(
                    secid, sourceContractVersion, writerVersion,
                    batch, batchFirstTime, batchLastTime, ct);
                rowsInsertedReported += reported;

                await progress.ReportAsync(taskId, rowsRead, batchLastTime, ct);
            }

            ClickHouseWriterLogMessages.RangeWritten(_logger, secid, rowsRead, rowsInsertedReported);
            return new RowWriteSummary(rowsRead, rowsInsertedReported, lastToken);
        }

        private async Task<(string Token, long Reported)> FlushAsync(
            string secid, string sourceContractVersion, string writerVersion,
            IReadOnlyList<object?[]> batch, DateTime firstTime, DateTime lastTime,
            CancellationToken ct)
        {
            string token = BuildToken(
                secid, firstTime, lastTime, batch.Count, sourceContractVersion, writerVersion);
            long reported = await _executor.InsertAsync(
                _map.Table, _map.Columns, _map.ColumnTypes, batch, token, ct);
            return (token, reported);
        }

        // {префикс}:{secid}:{время первой}:{время последней}:{row_count}:{версии}.
        // Префикс несёт вид и зерно (из карты). Инвариантный формат времени — границы пачки
        // воспроизводимы при повторе диапазона.
        private string BuildToken(
            string secid, DateTime firstTime, DateTime lastTime, int rowCount,
            string sourceContractVersion, string writerVersion)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{_map.TokenPrefix}:{secid}:{firstTime:yyyy-MM-ddTHH:mm:ss.fff}:{lastTime:yyyy-MM-ddTHH:mm:ss.fff}:{rowCount}:{sourceContractVersion}:{writerVersion}");
        }
    }
}
