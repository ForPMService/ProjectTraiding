using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineCalendarDayDto(
        DateOnly TradeDate,
        string Market,                 // stock | futures
        bool IsTraded,                 // из столбца is_traded (1/0 → да/нет)
        DateOnly? TradeSessionDate,
        string? Reason);               // H = праздник, W = выходной с weekend-сессией
}
