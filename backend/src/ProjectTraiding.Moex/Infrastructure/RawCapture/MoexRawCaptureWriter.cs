using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectTraiding.Moex.Options;

namespace ProjectTraiding.Moex.Infrastructure.RawCapture;

/// <summary>
/// Сохраняет сырой ответ MOEX в S3 по ключу.
/// Одна задача: положить байты в бакет. Не строит ключи, не решает что сохранять.
/// При ошибке записи — логирует, не бросает. S3 — вспомогательный контур.
/// </summary>
public sealed class MoexRawCaptureWriter
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<MoexRawCaptureWriter> _logger;
    private readonly RawCaptureOptions _options;

    public MoexRawCaptureWriter(
        IAmazonS3 s3Client,
        IOptions<RawCaptureOptions> options,
        ILogger<MoexRawCaptureWriter> logger)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Режим capture включает сохранение успешных ответов (Sample или All).
    /// Для error-path используй IsEnabled, для success-path — ShouldCaptureRaw.
    /// </summary>
    public bool ShouldCaptureRaw =>
        _options.Mode is CaptureMode.Sample or CaptureMode.All;

    /// <summary>
    /// Режим capture выключен — ничего не делаем.
    /// </summary>
    public bool IsEnabled => _options.Mode != CaptureMode.Off;

    /// <summary>
    /// Сохраняет сырые байты в S3. При любой ошибке — лог, не exception.
    /// </summary>
    /// <param name="objectKey">Полный ключ объекта (например moex/errors/schema-mismatch/alg/candles/...).</param>
    /// <param name="rawBody">Сырое тело ответа MOEX.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task TryCaptureAsync(
        string objectKey,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken ct)
    {
        if (_options.Mode == CaptureMode.Off)
        {
            return;
        }

        try
        {
            using MemoryStream stream = new MemoryStream(rawBody.ToArray(), writable: false);

            PutObjectRequest request = new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                InputStream = stream,
                ContentType = "application/json"
            };

            await _s3Client.PutObjectAsync(request, ct);

            RawCaptureLogMessages.CaptureSucceeded(_logger, objectKey, rawBody.Length);
        }
        catch (Exception ex)
        {
            RawCaptureLogMessages.CaptureFailed(_logger, ex, objectKey, rawBody.Length, ex.GetType().Name, ex.Message);
        }
    }
    /// <summary>
    /// Сохраняет Stream в S3. Для многостраничных ответов (NDJSON-аккумулятор).
    /// Stream.Position должен быть 0 перед вызовом.
    /// </summary>
    public async Task TryCaptureAsync(
        string objectKey,
        Stream rawBodyStream,
        long rawBodyLength,
        string contentType,
        CancellationToken ct)
    {
        if (_options.Mode == CaptureMode.Off)
        {
            return;
        }

        try
        {
            PutObjectRequest request = new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                InputStream = rawBodyStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request, ct);

            int loggedLength = rawBodyLength > int.MaxValue
                ? int.MaxValue : (int)rawBodyLength;
            RawCaptureLogMessages.CaptureSucceeded(_logger, objectKey, loggedLength);
        }
        catch (Exception ex)
        {
            int loggedLength = rawBodyLength > int.MaxValue
                ? int.MaxValue : (int)rawBodyLength;
            RawCaptureLogMessages.CaptureFailed(
                _logger, ex, objectKey, loggedLength,
                ex.GetType().Name, ex.Message);
        }
    }
}
