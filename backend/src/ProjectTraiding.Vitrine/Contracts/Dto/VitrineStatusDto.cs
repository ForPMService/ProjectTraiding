using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineStatusDto(
        DateOnly AsOfDate,
        int InstrumentsTotal,
        int InstrumentsStock,
        int InstrumentsFutures,
        bool? StockTradingToday,
        bool? FuturesTradingToday,
        int TariffsCount,
        int RelationsCount);
}
