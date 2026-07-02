using System.Buffers;

namespace ProjectTraiding.Moex.Infrastructure.Buffers
{
    public readonly struct RentedBuffer: IDisposable
    {
        readonly byte[] _buffer;
        readonly int _length;
        

        private RentedBuffer(int length, byte[] buffer)
        {
            _length = length;
            _buffer = buffer;
        }
        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

        /// <summary>
        /// Read-only memory view над тем же арендованным массивом и длиной, что и Span.
        /// Подходит для API, которым нужен ReadOnlyMemory&lt;byte&gt;, и позволяет передать
        /// содержимое без .ToArray() и без лишнего копирования буфера.
        /// </summary>
        public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, _length);
        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }

        /// <summary>
        /// Читает поток ДО ФАКТИЧЕСКОГО КОНЦА (Read==0), а не до объявленной длины.
        /// contentLength — лишь подсказка начального размера буфера (обычно Content-Length);
        /// если тела оказалось больше, массив дорастает. Так ни заниженный, ни завышенный,
        /// ни отсутствующий Content-Length не портит результат — читается ровно то, что
        /// реально прислал сервер. Чтение накрыто сторожем bodyReadTimeout: связанный
        /// с ct источник отмены рвёт зависшее чтение тела; вызывающий транслирует это
        /// в MoexTimeoutException(source="body_read").
        /// </summary>
        public static async Task<RentedBuffer> RentFromStreamAsync(
            Stream stream,
            int contentLengthHint,
            TimeSpan bodyReadTimeout,
            CancellationToken cancellationToken)
        {
            // Подсказка размера не может быть нулевой/отрицательной — берём разумный минимум.
            int initialCapacity = contentLengthHint > 0 ? contentLengthHint : 64 * 1024;

            using CancellationTokenSource timeoutCts = new(bodyReadTimeout);
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            byte[] arr = ArrayPool<byte>.Shared.Rent(initialCapacity);
            int position = 0;
            try
            {
                while (true)
                {
                    // Буфер заполнен — удваиваем ёмкость и переносим прочитанное.
                    if (position == arr.Length)
                    {
                        byte[] bigger = ArrayPool<byte>.Shared.Rent(arr.Length * 2);
                        Array.Copy(arr, bigger, position);
                        ArrayPool<byte>.Shared.Return(arr);
                        arr = bigger;
                    }

                    int read = await stream.ReadAsync(
                        arr.AsMemory(position, arr.Length - position), linkedCts.Token);

                    if (read == 0)
                        break; // фактический конец потока

                    position += read;
                }

                return new RentedBuffer(position, arr);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                // Отмена именно по нашему сторожу тела, а не по внешнему токену.
                ArrayPool<byte>.Shared.Return(arr);
                throw new TimeoutException(
                    $"MOEX body read exceeded {bodyReadTimeout.TotalSeconds:0.#}s");
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(arr);
                throw;
            }
        }
    }
}