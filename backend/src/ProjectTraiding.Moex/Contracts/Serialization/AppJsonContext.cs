using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Operations;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Moex.Contracts.Serialization;

[JsonSerializable(typeof(CalendarOffDaysMarketDTO))]         // CalendarEndpoints
[JsonSerializable(typeof(List<CalendarOffDaysMarketDTO>))]   // CalendarEndpoints
[JsonSerializable(typeof(LoadResultDto))]                    // InstrumentCardEndpoints, CalendarEndpoints
[JsonSerializable(typeof(LoadResultDto[]))]                  // InstrumentCardEndpoints (bootstrap)
public partial class AppJsonContext : JsonSerializerContext
{
}
