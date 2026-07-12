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

    /// <summary>
    /// Сумма байтов, принятых в фазе ожидания CONNECTED: сердцебиения плюс служебный кадр.
    /// Отдельно от CapturedBytes — у них разный смысл, и смешивать их значит врать в отчёте.
    /// </summary>
    public long HandshakeBytes { get; init; }

    /// <summary>Сумма байтов, принятых ПОСЛЕ SUBSCRIBE. Только фаза сбора.</summary>
    public long CapturedBytes { get; init; }

    /// <summary>
    /// Подпротокол, согласованный сервером. Мы просим STOMP; что выбрала биржа —
    /// сведение, а не подробность. Может оказаться пустым: сервер вправе не выбрать ничего.
    /// </summary>
    public string? NegotiatedSubProtocol { get; init; }

    /// <summary>Транспортный тип ответа на CONNECT: Text, Binary или Close.</summary>
    public string? HandshakeMessageType { get; init; }

    /// <summary>
    /// Число двоичных сообщений ПОСЛЕ SUBSCRIBE, полностью принятых в пределах
    /// установленного объёма, включая сердцебиения. Сообщение, оборванное пределом объёма,
    /// сюда не входит. Рукопожатие не входит тоже.
    ///
    /// Транспортный тип есть свойство сообщения, а не его содержимого: двоичный кадр —
    /// нормальная упаковка текста STOMP, а не признак неисправности.
    /// </summary>
    public int BinaryMessagesReceived { get; init; }

    /// <summary>
    /// Число сердцебиений после SUBSCRIBE.
    ///
    /// Без этого счётчика отчёт не различает два разных положения: биржа молчит вовсе
    /// (соединение мертво либо подписка не дошла) — и биржа исправно шлёт сердцебиения,
    /// но данных не даёт. Второе само по себе ещё ничего не доказывает: данных может не быть
    /// потому, что не было изменений, потому что инструмент неактивен, или потому что
    /// подписка не сработала. Счётчик отделяет живое соединение от мёртвого, не более того.
    /// </summary>
    public int HeartbeatsReceived { get; init; }

    /// <summary>
    /// Начало содержимого, которое не удалось прочитать как UTF-8 — в шестнадцатеричном виде.
    /// </summary>
    public string? UnreadableContentPreview { get; init; }

    public bool Truncated { get; init; }
    public string? TerminationReason { get; init; }
    public string? CloseStatus { get; init; }
    public string? CloseDescription { get; init; }
    public TimeSpan Elapsed { get; init; }
}
