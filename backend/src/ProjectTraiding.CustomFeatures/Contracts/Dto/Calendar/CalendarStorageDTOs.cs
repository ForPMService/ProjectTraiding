namespace ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;

public record CalendarDayWriteDTO
{
    public DateOnly TradeDate { get; init; }
    public string Market { get; init; } = string.Empty;
    public int IsTraded { get; init; }
    public DateOnly? TradeSessionDate { get; init; }
    public string? Reason { get; init; }
    public int? EngineIsWorkDay { get; init; }
    public string DataSource { get; init; } = string.Empty;
    public DateTime? MoexUpdateTime { get; init; }
}

public record TradingPeriodWriteDTO
{
    public DateOnly TradeDate { get; init; }
    public string Market { get; init; } = string.Empty;
    public string BoardId { get; init; } = string.Empty;
    public string SecId { get; init; } = string.Empty;
    public short? Session { get; init; }
    public string PeriodType { get; init; } = string.Empty;
    public DateTime TimeFrom { get; init; }
    public DateTime? TimeTill { get; init; }
    public DateTime? MoexUpdateTime { get; init; }
}

public record InstrumentBoardIntervalDTO
{
    public string Market { get; init; } = string.Empty;
    public string SecId { get; init; } = string.Empty;
    public string BoardId { get; init; } = string.Empty;
    public DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTill { get; init; }
}

public record FuturesExpirationDTO
{
    public string SecId { get; init; } = string.Empty;
    public string? AssetCode { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public string? ExpirationType { get; init; }
    public DateOnly? EndDate { get; init; }
    public short? WeekendSession { get; init; }
}

public record SplitWriteDTO
{
    public DateOnly TradeDate { get; init; }
    public string SecId { get; init; } = string.Empty;
    public int BeforeQty { get; init; }
    public int AfterQty { get; init; }
}
