using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Diagnostics.Contracts;

[JsonSerializable(typeof(List<CandlesDTO>))]
[JsonSerializable(typeof(List<SuperCandlesTradeStats5mDTO>))]
[JsonSerializable(typeof(List<SuperCandlesOrderBookStats5mDTO>))]
[JsonSerializable(typeof(List<SuperCandlesOrderStats5mDTO>))]
[JsonSerializable(typeof(List<SuperCandlesFuturesTradeStats5mDTO>))]
[JsonSerializable(typeof(List<SuperCandlesFuturesOrderBookStats5mDTO>))]
[JsonSerializable(typeof(List<FutoiDTO>))]
[JsonSerializable(typeof(List<Hi2AssetDTO>))]
[JsonSerializable(typeof(List<Hi2FuturesDTO>))]
[JsonSerializable(typeof(List<MegaAlertsAssetsDTO>))]
[JsonSerializable(typeof(List<MegaAlertsFuturesDTO>))]
[JsonSerializable(typeof(CandlesDTO))]
[JsonSerializable(typeof(SuperCandlesTradeStats5mDTO))]
[JsonSerializable(typeof(SuperCandlesOrderBookStats5mDTO))]
[JsonSerializable(typeof(SuperCandlesOrderStats5mDTO))]
[JsonSerializable(typeof(SuperCandlesFuturesTradeStats5mDTO))]
[JsonSerializable(typeof(SuperCandlesFuturesOrderBookStats5mDTO))]
[JsonSerializable(typeof(FutoiDTO))]
[JsonSerializable(typeof(Hi2AssetDTO))]
[JsonSerializable(typeof(Hi2FuturesDTO))]
[JsonSerializable(typeof(MegaAlertsAssetsDTO))]
[JsonSerializable(typeof(MegaAlertsFuturesDTO))]
public partial class DiagnosticsJsonContext : JsonSerializerContext
{
}
