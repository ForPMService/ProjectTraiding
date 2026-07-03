using System.Buffers;

namespace ProjectTraiding.Moex.Infrastructure.Buffers
{
    public readonly struct RentedBuffer : IDisposable
    {
        /// <summary>
        /// Верхний предел роста буфера чтения тела — страховка от неограниченного разрастания.
        /// Ответы MOEX такого размера не достигают; превышение означает аномалию (сбойный
        /// поток, повреждённое сжатие), при которой честнее оборвать чтение, чем раздувать
        /// память до отказа. Сторож времени (BodyReadTimeout) ограничивает длительность
        /// чтения, эта константа — его объём.
        /// </summary>
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
        /// если тела оказалось больше, массив дорастает удвоением, но не выше страховки
        /// MaxBufferBytes. Так ни заниженный, ни завышенный, ни отсутствующий Content-Length
        /// не портит результат — читается ровно то, что реально прислал сервер, но в разумных
        /// пределах. Чтение накрыто сторожем bodyReadTimeout: связанный с ct источник отмены
        /// рвёт зависшее чтение тела; вызывающий транслирует это в MoexTimeoutException(source="body_read").
        /// Превышение страховки объёма — отдельная аномалия, обрывается InvalidOperationException.
        /// </summary>
        public static async Task<RentedBuffer> RentFromStreamAsync(
            Stream stream,
            int contentLengthHint,
            TimeSpan bodyReadTimeout,
            CancellationToken cancellationToken)
        {
            // Подсказка размера не может быть нулевой/отрицательной — берём разумный минимум;
            // и не может быть выше страховки — иначе аренда сразу превысит предел.
            int initialCapacity = contentLengthHint > 0 ? contentLengthHint : MinBufferBytes;
            if (initialCapacity < MinBufferBytes)
                initialCapacity = MinBufferBytes;
            if (initialCapacity > MaxBufferBytes)
                initialCapacity = MaxBufferBytes;

            using CancellationTokenSource timeoutCts = new(bodyReadTimeout);
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            byte[] arr = ArrayPool<byte>.Shared.Rent(initialCapacity);
            int position = 0;
            try
            {
                while (true)
                {
                    // Буфер заполнен — удваиваем ёмкость и переносим прочитанное, но не выше
                    // страховки. Достигли предела и всё ещё есть данные — обрываем аномалию.
                    if (position == arr.Length)
                    {
                        if (arr.Length >= MaxBufferBytes)
                        {
                            // Не возвращаем массив здесь: это сделает завершающий catch,
                            // иначе получился бы двойной возврат в пул.
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