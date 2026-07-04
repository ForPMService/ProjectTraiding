using System;

namespace ProjectTraiding.Moex.Infrastructure.RawCapture
{
    /// <summary>
    /// Многостраничный захват успешных ответов для методов, возвращающих
    /// IAsyncEnumerable&lt;List&lt;T&gt;&gt;. Каждая страница или день пишется в объектное
    /// хранилище сразу отдельным объектом page=N.json, без накопления в памяти. Контекст
    /// ключа фиксируется при создании. При ShouldCaptureRaw=false запись не выполняется.
    /// </summary>
    public sealed class RawCaptureAccumulator
    {
        private readonly MoexRawCaptureWriter _writer;
        private readonly string _client;
        private readonly string _dataType;
        private readonly string? _market;
        private readonly string? _secid;
        private readonly string _runId;

        public RawCaptureAccumulator(
            MoexRawCaptureWriter writer,
            string client,
            string dataType,
            string? market,
            string? secid,
            string runId)
        {
            _writer = writer;
            _client = client;
            _dataType = dataType;
            _market = market;
            _secid = secid;
            _runId = runId;
        }

        /// <summary>
        /// Пишет сырые байты одной страницы или дня отдельным объектом сразу.
        /// Вызывать ВНУТРИ using(rentedArr) — пока арендованный буфер жив, до возврата в пул.
        /// </summary>
        public async Task AppendPageAsync(
            ReadOnlyMemory<byte> pageBytes,
            int pageNumber,
            CancellationToken ct)
        {
            if (!_writer.ShouldCaptureRaw)
            {
                return;
            }

            string key = RawCaptureKeyBuilder.BuildRawKey(
                _client,
                _dataType,
                _market,
                _secid,
                DateOnly.FromDateTime(DateTime.UtcNow),
                _runId,
                RawCaptureKeyBuilder.PageFileName(pageNumber));

            await _writer.TryCaptureAsync(key, pageBytes, ct);
        }
    }
}
