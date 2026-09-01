namespace ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;

public record CalendarOffDaysMarketDTO
{
    public DateOnly TradeDate { get; init; }
    public int? IsTraded { get; init; }
    public DateOnly? TradeSessionDate { get; init; }
    public string? Reason { get; init; }
    public DateTime? UpdateTime { get; init; }
}
