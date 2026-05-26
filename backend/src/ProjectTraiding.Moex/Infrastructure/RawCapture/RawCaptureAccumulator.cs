using System;


namespace ProjectTraiding.Moex.Infrastructure.RawCapture
{
    /// <summary>
    /// Multi-page success capture. Для методов, возвращающих IAsyncEnumerable&lt;List&lt;T&gt;&gt;.
    /// Копит сырые байты всех страниц/дней в MemoryStream.
    /// После завершения цикла пишет один response.ndjson в S3.
    /// Если ShouldCaptureRaw=false, не создаёт MemoryStream и не копирует байты.
    /// </summary>
    public sealed class RawCaptureAccumulator: IDisposable
    {
        private readonly MemoryStream? _stream;
    private readonly MoexRawCaptureWriter _writer;

    public RawCaptureAccumulator(MoexRawCaptureWriter writer)
    {
        _writer = writer;
        _stream = writer.ShouldCaptureRaw ? new MemoryStream() : null;
    }

    /// <summary>
    /// Добавить сырые байты одной страницы/дня.
    /// Вызывать ВНУТРИ using(rentedArr) — пока буфер жив.
    /// </summary>
    public void AppendPage(ReadOnlyMemory<byte> pageBytes)
    {
        if (_stream is null)
        {
            return;
        }

        _stream.Write(pageBytes.Span);
        _stream.WriteByte((byte)'\n');
    }

    /// <summary>
    /// Записать накопленный NDJSON как один объект в S3.
    /// Вызывать ПОСЛЕ полного завершения цикла пагинации.
    /// </summary>
    public async Task FlushNdjsonAsync(
        string client,
        string dataType,
        string? market,
        string? secid,
        string runId,
        CancellationToken ct)
    {
        if (_stream is null || _stream.Length == 0)
        {
            return;
        }

        string key = RawCaptureKeyBuilder.BuildRawKey(
            client,
            dataType,
            market,
            secid,
            DateOnly.FromDateTime(DateTime.UtcNow),
            runId,
            RawCaptureKeyBuilder.ResponseNdjsonFileName());

        _stream.Position = 0;

        await _writer.TryCaptureAsync(
            key,
            _stream,
            _stream.Length,
            "application/x-ndjson",
            ct);
    }

    public void Dispose()
    {
        _stream?.Dispose();
    }
    }
}
