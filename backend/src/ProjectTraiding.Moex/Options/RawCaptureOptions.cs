namespace ProjectTraiding.Moex.Options;

/// <summary>
/// Настройки S3 raw-capture для сырых ответов MOEX.
/// Секция конфигурации: RawCapture.
/// </summary>
public sealed class RawCaptureOptions
{
    /// <summary>S3-совместимый endpoint (например http://localhost:3900).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Регион S3 (для Garage — "garage").</summary>
    public string Region { get; set; } = "garage";

    /// <summary>Имя бакета.</summary>
    public string Bucket { get; set; } = "";

    /// <summary>Access key.</summary>
    public string AccessKey { get; set; } = "";

    /// <summary>Secret key.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Режим capture: Off, FailedOnly, Sample, All.</summary>
    public CaptureMode Mode { get; set; } = CaptureMode.Off;
}

/// <summary>
/// Режим сохранения сырых ответов MOEX в S3.
/// </summary>
public enum CaptureMode
{
    /// <summary>Ничего не сохраняем.</summary>
    Off,

    /// <summary>Только проблемные ответы (ошибки парсинга, HTTP-ошибки, пустые данные).</summary>
    FailedOnly,

    /// <summary>Часть ответов (контрольные снимки).</summary>
    Sample,

    /// <summary>Все ответы в рамках запуска.</summary>
    All
}
