using Microsoft.Extensions.Logging;

namespace ProjectTraiding.Moex.Infrastructure.RawCapture;

/// <summary>
/// Лог-события raw-capture контура.
/// EventId 160–169: зарезервировано за raw-capture.
/// </summary>
public static partial class RawCaptureLogMessages
{
    [LoggerMessage(
        EventId = 160,
        EventName = "RawCaptureSucceeded",
        Level = LogLevel.Debug,
        Message = "Raw capture saved: key={ObjectKey}, size={BodySize}.")]
    public static partial void CaptureSucceeded(ILogger logger, string objectKey, int bodySize);

    [LoggerMessage(
        EventId = 161,
        EventName = "RawCaptureFailed",
        Level = LogLevel.Warning,
        Message = "Raw capture failed: key={ObjectKey}, size={BodySize}, errorType={ErrorType}, message={ErrorMessage}.")]
    public static partial void CaptureFailed(ILogger logger, Exception exception, string objectKey, int bodySize, string errorType, string errorMessage);
}
