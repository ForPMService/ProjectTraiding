using System;


namespace ProjectTraiding.Moex.Infrastructure.RawCapture
{
    /// <summary>
    /// Single-page success capture. Для методов, возвращающих Task&lt;T&gt;.
    /// Пишет response.json напрямую из rentedArr.Memory без промежуточных копий.
    /// </summary>
    public static class RawCaptureHelper
    {
        public static async Task CaptureSingleAsync(
        MoexRawCaptureWriter writer,
        string client,
        string dataType,
        string? market,
        string? secid,
        string runId,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken ct)
        {
            if (!writer.ShouldCaptureRaw)
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
                RawCaptureKeyBuilder.ResponseFileName());

            await writer.TryCaptureAsync(key, rawBody, ct);
        }
    }
}
