using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.Contracts.Dto.Operations;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Endpoints;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Moex.Contracts.Serialization
{
    // ── Списки DTO — для endpoint'ов, которые возвращают Results.Json(list, AppJsonContext.Default.List...) ──
    // Используются текущими endpoint'ами, которые копят все страницы в List<T> и отдают целиком.
    // Останутся нужны, пока не все endpoint'ы переведены на потоковую отдачу.

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

    // Списки DTO календаря
    [JsonSerializable(typeof(List<CalendarOffDaysMarketDTO>))]

    // ── Одиночные DTO — для поштучной сериализации ──
    // Нужны source generator'у, чтобы знать как сериализовать один объект.
    // Используются IAsyncEnumerable-endpoint'ами (фреймворк сериализует по одному элементу)
    // и могут использоваться в будущем для ручной сериализации через Utf8JsonWriter.

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

    // DTO календаря
    [JsonSerializable(typeof(CalendarOffDaysMarketDTO))]

    // ── IAsyncEnumerable<T> — для потоковой отдачи через встроенный механизм ASP.NET ──
    // Когда endpoint возвращает IAsyncEnumerable<T>, фреймворк вызывает
    // JsonSerializer.SerializeAsync, который внутри использует Utf8JsonWriter.
    // Source generator должен знать про этот тип, иначе AOT не сможет сериализовать.
    // Добавлять по мере перевода endpoint'ов на потоковую отдачу.

    [JsonSerializable(typeof(IAsyncEnumerable<CandlesDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<SuperCandlesTradeStats5mDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<SuperCandlesOrderStats5mDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<SuperCandlesOrderBookStats5mDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<SuperCandlesFuturesTradeStats5mDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<SuperCandlesFuturesOrderBookStats5mDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<FutoiDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<Hi2AssetDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<Hi2FuturesDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<MegaAlertsAssetsDTO>))]
    [JsonSerializable(typeof(IAsyncEnumerable<MegaAlertsFuturesDTO>))]

    [JsonSerializable(typeof(RealtimeOrderbookParseResult))]
    [JsonSerializable(typeof(RealtimeTradesParseResult<RealtimeTradesStockDTO>))]
    [JsonSerializable(typeof(RealtimeTradesParseResult<RealtimeTradesFuturesDTO>))]

    [JsonSerializable(typeof(RealtimeOrderbookRowDTO))]
    [JsonSerializable(typeof(RealtimeDataVersionDTO))]
    [JsonSerializable(typeof(RealtimeTradesStockDTO))]
    [JsonSerializable(typeof(RealtimeTradesFuturesDTO))]
    [JsonSerializable(typeof(RealtimeTradesYieldsDTO))]

    [JsonSerializable(typeof(List<RealtimeOrderbookRowDTO>))]
    [JsonSerializable(typeof(List<RealtimeTradesStockDTO>))]
    [JsonSerializable(typeof(List<RealtimeTradesFuturesDTO>))]
    [JsonSerializable(typeof(List<RealtimeTradesYieldsDTO>))]

    [JsonSerializable(typeof(RealtimeDiagnosticEndpoints.OrderbookPollReport))]
    [JsonSerializable(typeof(RealtimeDiagnosticEndpoints.OrderbookSnapshot))]

    [JsonSerializable(typeof(RealtimeDiagnosticEndpoints.TradesPollReport))]
    [JsonSerializable(typeof(RealtimeDiagnosticEndpoints.TradesSnapshot))]

    [JsonSerializable(typeof(RealtimeDiagnosticEndpoints.CandlesPollReport))]
    [JsonSerializable(typeof(RealtimeDiagnosticEndpoints.CandlesSnapshot))]

    
    [JsonSerializable(typeof(List<StockInstrumentCardDTO>))]
    [JsonSerializable(typeof(List<FuturesInstrumentCardDTO>))]
    [JsonSerializable(typeof(StockInstrumentCardDTO))]
    [JsonSerializable(typeof(FuturesInstrumentCardDTO))]
        [JsonSerializable(typeof(LoadResultDto))]
        [JsonSerializable(typeof(LoadResultDto[]))]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }
}