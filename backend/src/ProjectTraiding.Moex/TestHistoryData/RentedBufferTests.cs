using System.Buffers;
using History_DataMoex.Infrastructure.Buffers;

namespace TestHistoryData;

/// <summary>
/// Тесты RentedBuffer (Phase 4.3).
/// 
/// RentedBuffer — readonly struct, обёртка над ArrayPool&lt;byte&gt;.Shared.
/// Проверяем: корректность Span, поведение на пустом/коротком/большом stream,
/// безопасность двойного Dispose.
/// </summary>
public class RentedBufferTests
{
    // ═══════════════════════════════════════════════════════════
    // 1. Span.Length == количество записанных байт, не размер массива пула
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Span_HasCorrectLength()
    {
        // Arrange: 37 байт — не степень двойки, пул выделит больше (64 или 128)
        byte[] source = new byte[37];
        Random.Shared.NextBytes(source);
        using var stream = new MemoryStream(source);

        // Act
        using var rented = await RentedBuffer.RentFromStreamAsync(stream, source.Length, CancellationToken.None);

        // Assert: Span ровно 37, не 64/128
        Assert.Equal(source.Length, rented.Span.Length);
    }

    // ═══════════════════════════════════════════════════════════
    // 2. Байты в Span совпадают с исходными
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Span_ContainsCorrectData()
    {
        // Arrange
        byte[] source = new byte[256];
        for (int i = 0; i < source.Length; i++)
            source[i] = (byte)(i % 256);
        using var stream = new MemoryStream(source);

        // Act
        using var rented = await RentedBuffer.RentFromStreamAsync(stream, source.Length, CancellationToken.None);

        // Assert: побайтовое сравнение
        Assert.True(rented.Span.SequenceEqual(source),
            "Содержимое Span не совпадает с исходным массивом.");
    }

    // ═══════════════════════════════════════════════════════════
    // 3. Пустой stream → Length == 0, без exception
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task EmptyStream_ReturnsZeroLength()
    {
        // Arrange: пустой stream, но length > 0 (имитация Content-Length больше реального тела)
        using var stream = new MemoryStream(Array.Empty<byte>());

        // Act: stream сразу вернёт bytesRead == 0, цикл выйдет
        using var rented = await RentedBuffer.RentFromStreamAsync(stream, 1024, CancellationToken.None);

        // Assert
        Assert.Equal(0, rented.Span.Length);
    }

    // ═══════════════════════════════════════════════════════════
    // 4. Stream короче заявленного length — читаем сколько есть
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task StreamShorterThanLength_ReadsAvailableBytes()
    {
        // Arrange: реальных данных 100 байт, а length передаём 500
        byte[] source = new byte[100];
        Random.Shared.NextBytes(source);
        using var stream = new MemoryStream(source);

        // Act
        using var rented = await RentedBuffer.RentFromStreamAsync(stream, 500, CancellationToken.None);

        // Assert: прочитаны только 100 реальных байт
        Assert.Equal(100, rented.Span.Length);
        Assert.True(rented.Span.SequenceEqual(source));
    }

    // ═══════════════════════════════════════════════════════════
    // 5. Два вызова Dispose() без exception
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Dispose_DoesNotThrowOnDoubleDispose()
    {
        // Arrange
        byte[] source = [1, 2, 3];
        using var stream = new MemoryStream(source);
        var rented = await RentedBuffer.RentFromStreamAsync(stream, source.Length, CancellationToken.None);

        // Act & Assert: первый Dispose нормально, второй не должен бросить
        rented.Dispose();

        var ex = Record.Exception(() => rented.Dispose());
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════
    // 6. Большой payload (200 КБ) читается корректно
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task LargePayload_CorrectRead()
    {
        // Arrange: 200 КБ — типичный ответ MOEX ALGOPACK
        const int size = 200 * 1024;
        byte[] source = new byte[size];
        Random.Shared.NextBytes(source);

        // SlowStream отдаёт данные порциями по 4 КБ — имитация сетевого stream
        using var stream = new SlowStream(source, chunkSize: 4096);

        // Act
        using var rented = await RentedBuffer.RentFromStreamAsync(stream, size, CancellationToken.None);

        // Assert
        Assert.Equal(size, rented.Span.Length);
        Assert.True(rented.Span.SequenceEqual(source),
            "200 КБ payload: содержимое не совпадает.");
    }

    // ═══════════════════════════════════════════════════════════
    // 7. CancellationToken отменён — массив возвращается в пул
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelledToken_ThrowsAndDoesNotLeak()
    {
        // Arrange: stream, который не успеет отдать данные
        byte[] source = new byte[1024];
        using var stream = new MemoryStream(source);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // уже отменён

        // Act & Assert: OperationCanceledException, массив вернулся в пул (не утёк)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RentedBuffer.RentFromStreamAsync(stream, source.Length, cts.Token));
    }

    // ═══════════════════════════════════════════════════════════
    // Helper: stream, отдающий данные порциями (имитация сети)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// MemoryStream, который за один ReadAsync отдаёт не больше chunkSize байт.
    /// Имитирует поведение сетевого stream (TCP порции).
    /// </summary>
    private sealed class SlowStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private int _position;

        public SlowStream(byte[] data, int chunkSize)
        {
            _data = data;
            _chunkSize = chunkSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(count, Math.Min(_chunkSize, _data.Length - _position));
            if (available <= 0) return 0;
            Array.Copy(_data, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }

        // Обязательные для Stream, но не используются в тестах
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
