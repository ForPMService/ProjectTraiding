namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Значения метки телеметрии <c>operation</c> — производственные операции контура MOEX.
/// Операция называет предметное действие, а не метод C# и не адрес источника:
/// имя остаётся стабильным при переименовании метода и при смене адреса.
///
/// Диагностические и отладочные точки в каталог не входят: они не являются
/// производственными операциями и метрик не получают.
/// </summary>
public static class MoexOperations
{
    /// <summary>Карточки инструментов: акции через ISS, фьючерсы через APIM.</summary>
    public const string ReferenceInstrumentsFetch = "reference.instruments.fetch";

    /// <summary>Историческая страница свечей.</summary>
    public const string HistoryCandlesFetch = "history.candles.fetch";

    /// <summary>Историческая курсорная страница любого из девяти видов.</summary>
    public const string HistoryCursorFetch = "history.cursor.fetch";

    /// <summary>Дневная страница открытого интереса.</summary>
    public const string HistoryFutoiFetch = "history.futoi.fetch";

    /// <summary>Нерабочие дни торгового календаря.</summary>
    public const string CalendarOffDaysFetch = "calendar.offdays.fetch";

    /// <summary>Опрос ленты сделок одного инструмента.</summary>
    public const string RealtimeTradesPoll = "realtime.trades.poll";

    /// <summary>Опрос снимка стакана одного инструмента.</summary>
    public const string RealtimeOrderbookPoll = "realtime.orderbook.poll";

    /// <summary>Опрос текущих свечей одного инструмента.</summary>
    public const string RealtimeCandlesPoll = "realtime.candles.poll";
}
