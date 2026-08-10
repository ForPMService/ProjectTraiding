using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Diagnostics.Contracts;

[JsonSerializable(typeof(List<CandlesDTO>))]
[JsonSerializable(typeof(CandlesDTO))]
public partial class DiagnosticsJsonContext : JsonSerializerContext
{
}
