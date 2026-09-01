using System.Buffers;
using ProjectTraiding.CustomFeatures.Errors;

namespace ProjectTraiding.CustomFeatures.Infrastructure.Buffers;

public readonly struct RentedBuffer : IDisposable
{
    private const int MaxBufferBytes = 64 * 1024 * 1024;
    private const int MinBufferBytes = 64 * 1024;

    readonly byte[] _buffer;
    readonly int _length;

    private RentedBuffer(int length, byte[] buffer)
    {
        _length = length;
        _buffer = buffer;
    }

    public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

    public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, _length);

    public void Dispose()
    {
        if (_buffer != null)
            ArrayPool<byte>.Shared.Return(_buffer);
    }

    public static async Task<RentedBuffer> RentFromResponseAsync(
        HttpResponseMessage response,
        TimeSpan bodyReadTimeout,
        string endpoint,
        CancellationToken cancellationToken)
    {
        int contentLength = (int)(response.Content.Headers.ContentLength ?? 1_048_576);
        return await RentFromStreamAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            contentLength,
            bodyReadTimeout,
            endpoint,
            cancellationToken);
    }

    public static async Task<RentedBuffer> RentFromStreamAsync(
        Stream stream,
        int contentLengthHint,
        TimeSpan bodyReadTimeout,
        string endpoint,
        CancellationToken cancellationToken)
    {
        int initialCapacity = contentLengthHint > 0 ? contentLengthHint : MinBufferBytes;
        if (initialCapacity < MinBufferBytes)
            initialCapacity = MinBufferBytes;
        if (initialCapacity > MaxBufferBytes)
            initialCapacity = MaxBufferBytes;

        using CancellationTokenSource bodyReadCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyReadCts.CancelAfter(bodyReadTimeout);

        byte[] arr = ArrayPool<byte>.Shared.Rent(initialCapacity);
        int position = 0;
        try
        {
            while (true)
            {
                if (position == arr.Length)
                {
                    if (arr.Length >= MaxBufferBytes)
                    {
                        throw new InvalidOperationException(
                            $"MOEX response body exceeded safety cap of {MaxBufferBytes / (1024 * 1024)} MB.");
                    }

                    int newLength = Math.Min(arr.Length * 2, MaxBufferBytes);
                    byte[] bigger = ArrayPool<byte>.Shared.Rent(newLength);
                    Array.Copy(arr, bigger, position);
                    ArrayPool<byte>.Shared.Return(arr);
                    arr = bigger;
                }

                int read = await stream.ReadAsync(
                    arr.AsMemory(position, arr.Length - position), bodyReadCts.Token);

                if (read == 0)
                    break;

                position += read;
            }

            return new RentedBuffer(position, arr);
        }
        catch (OperationCanceledException ex) when (bodyReadCts.IsCancellationRequested
                                                    && !cancellationToken.IsCancellationRequested)
        {
            ArrayPool<byte>.Shared.Return(arr);
            throw new CalendarTimeoutException(
                $"MOEX body read exceeded {bodyReadTimeout.TotalSeconds:0.#}s for {endpoint}",
                endpoint,
                "body_read",
                ex);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(arr);
            throw;
        }
    }
}
