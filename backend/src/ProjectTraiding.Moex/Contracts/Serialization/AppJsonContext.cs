using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Operations;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Moex.Contracts.Serialization;

[JsonSerializable(typeof(CandlesDTO))]                       // RealtimeLatestWriter (Redis)
[JsonSerializable(typeof(RealtimeTradesStockDTO))]           // RealtimeLatestWriter (Redis)
[JsonSerializable(typeof(RealtimeTradesFuturesDTO))]         // RealtimeLatestWriter (Redis)
[JsonSerializable(typeof(RealtimeOrderbookRowDTO))]          // элемент списка ниже
[JsonSerializable(typeof(List<RealtimeOrderbookRowDTO>))]    // RealtimeLatestWriter (Redis)
[JsonSerializable(typeof(CalendarOffDaysMarketDTO))]         // CalendarEndpoints
[JsonSerializable(typeof(List<CalendarOffDaysMarketDTO>))]   // CalendarEndpoints
[JsonSerializable(typeof(LoadResultDto))]                    // InstrumentCardEndpoints, CalendarEndpoints
[JsonSerializable(typeof(LoadResultDto[]))]                  // InstrumentCardEndpoints (bootstrap)
[JsonSerializable(typeof(LoadProgressValue))]                // LoadProgressWriter (Redis)
public partial class AppJsonContext : JsonSerializerContext
{
}
