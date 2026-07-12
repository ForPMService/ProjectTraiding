namespace ProjectTraiding.Moex.Realtime;

/// <summary>
/// Отчёт пробника. Служебные кадры сохраняются ПОЛНЫМ СЫРЫМ ТЕКСТОМ, с заголовками:
/// у CONNECTED и RECEIPT тела не бывает, всё существенное — в заголовках.
/// </summary>
public record WebSocketProbeReport
{
    public bool Connected { get; init; }
    public string? RawConnectedFrame { get; init; }
    public string? RawReceiptFrame { get; init; }
    public string? ReceiptId { get; init; }
    public string? RawErrorFrame { get; init; }
    public string? RawUnexpectedHandshakeFrame { get; init; }
    public int UnparsedFrames { get; init; }
    public List<string> RawFrames { get; init; } = new();
    public int FramesReceived { get; init; }
    public long CapturedBytes { get; init; }
    public bool Truncated { get; init; }
    public string? TerminationReason { get; init; }
    public string? CloseStatus { get; init; }
    public string? CloseDescription { get; init; }
    public TimeSpan Elapsed { get; init; }
}
