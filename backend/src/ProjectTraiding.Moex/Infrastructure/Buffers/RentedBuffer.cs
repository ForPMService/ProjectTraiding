using System.Buffers;
using ProjectTraiding.Moex.Clients.Errors;

namespace ProjectTraiding.Moex.Infrastructure.Buffers
{
    public readonly struct RentedBuffer : IDisposable
    {
        /// <summary>
        /// Верхний предел фактически прочитанных байтов тела — страховка от неограниченного разрастания.
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

        public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, _length);

        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }

        /// <summary>
        /// Читает тело ответа в арендованный буфер. Подсказкой начального размера служит
        /// заголовок длины содержимого; при его отсутствии берётся мегабайт. Сама подсказка
        /// на результат не влияет — поток читается до фактического конца.
        /// </summary>
        public static async Task<RentedBuffer> RentFromResponseAsync(
            HttpResponseMessage response,
            TimeSpan bodyReadTimeout,
            string endpoint,
            CancellationToken cancellationToken)
        {
            long contentLength = response.Content.Headers.ContentLength ?? 1_048_576L;
            int contentLengthHint = contentLength <= 0
                ? 0
                : contentLength >= MaxBufferBytes
                    ? MaxBufferBytes
                    : (int)contentLength;

            return await RentFromStreamAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                contentLengthHint,
                bodyReadTimeout,
                endpoint,
                cancellationToken);
        }

        /// <summary>
        /// Читает поток ДО ФАКТИЧЕСКОГО КОНЦА (Read==0), а не до объявленной длины.
        /// contentLength — лишь подсказка начального размера буфера (обычно Content-Length);
        /// если тела оказалось больше, массив дорастает удвоением, но не выше страховки
        /// MaxBufferBytes. Так ни заниженный, ни завышенный, ни отсутствующий Content-Length
        /// не портит результат — читается ровно то, что реально прислал сервер, но в разумных
        /// пределах. Чтение накрыто сторожем bodyReadTimeout: связанный с ct источник отмены
        /// рвёт зависшее чтение тела; буфер транслирует это в MoexTimeoutException(source="body_read").
        /// Превышение страховки объёма — отдельная аномалия, обрывается InvalidOperationException.
        /// </summary>
        public static async Task<RentedBuffer> RentFromStreamAsync(
            Stream stream,
            int contentLengthHint,
            TimeSpan bodyReadTimeout,
            string endpoint,
            CancellationToken cancellationToken)
        {
            // Подсказка размера не может быть нулевой/отрицательной — берём разумный минимум;
            // и не может быть выше страховки — иначе аренда сразу превысит предел.
            int initialCapacity = contentLengthHint > 0 ? contentLengthHint : MinBufferBytes;
            if (initialCapacity < MinBufferBytes)
                initialCapacity = MinBufferBytes;
            if (initialCapacity > MaxBufferBytes)
                initialCapacity = MaxBufferBytes;

            // Один источник вместо двух: связанный с внешним токеном и со своим сроком.
            // Различить собственный сторож от внешней отмены это не мешает — ниже.
            using CancellationTokenSource bodyReadCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bodyReadCts.CancelAfter(bodyReadTimeout);

            byte[] arr = ArrayPool<byte>.Shared.Rent(initialCapacity);
            int position = 0;
            try
            {
                while (true)
                {
                    if (position == MaxBufferBytes)
                    {
                        // На ровно 64 МиБ разрешаем EOF; один асинхронный байт отличает его от превышения.
                        byte[] overflowProbe = new byte[1];
                        int overflowRead = await stream.ReadAsync(
                            overflowProbe.AsMemory(), bodyReadCts.Token);

                        if (overflowRead > 0)
                        {
                            throw new InvalidOperationException(
                                $"MOEX response body exceeded safety cap of {MaxBufferBytes / (1024 * 1024)} MB.");
                        }

                        break;
                    }

                    // ArrayPool может вернуть массив крупнее лимита; рост нужен только когда
                    // текущая физическая аренда заполнена до достижения лимита тела.
                    if (position == arr.Length)
                    {
                        int newLength = arr.Length > MaxBufferBytes / 2
                            ? MaxBufferBytes
                            : arr.Length * 2;
                        byte[] bigger = ArrayPool<byte>.Shared.Rent(newLength);

                        try
                        {
                            Array.Copy(arr, bigger, position);
                        }
                        catch
                        {
                            ArrayPool<byte>.Shared.Return(bigger);
                            throw;
                        }

                        byte[] previous = arr;
                        arr = bigger;
                        ArrayPool<byte>.Shared.Return(previous);
                    }

                    int writableBytes = Math.Min(arr.Length - position, MaxBufferBytes - position);
                    int read = await stream.ReadAsync(
                        arr.AsMemory(position, writableBytes), bodyReadCts.Token);

                    if (read == 0)
                        break; // фактический конец потока

                    position += read;
                }

                return new RentedBuffer(position, arr);
            }
            catch (OperationCanceledException ex) when (bodyReadCts.IsCancellationRequested
                                                        && !cancellationToken.IsCancellationRequested)
            {
                // Отмена именно по нашему сторожу тела, а не по внешнему токену.
                ArrayPool<byte>.Shared.Return(arr);
                throw new MoexTimeoutException(
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
}
