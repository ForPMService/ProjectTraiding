namespace ProjectTraiding.Moex.Contracts.Dto.Realtime
{
    /// <summary>
    /// Результат парсинга ответа orderbook endpoint-а MOEX real-time REST.
    /// 
    /// Содержит:
    ///   Rows — строки стакана (блок "orderbook");
    ///   DataVersion — служебный блок версии данных (блок "dataversion").
    /// </summary>
    public sealed record RealtimeOrderbookParseResult(
        List<RealtimeOrderbookRowDTO> Rows,
        RealtimeDataVersionDTO DataVersion);

    /// <summary>
    /// Результат парсинга ответа trades endpoint-а MOEX real-time REST.
    /// 
    /// Типизирован по виду сделки:
    ///   RealtimeTradesStockDTO для акций (15 колонок);
    ///   RealtimeTradesFuturesDTO для фьючерсов (13 колонок).
    /// 
    /// Содержит:
    ///   Rows — строки сделок (блок "trades");
    ///   DataVersion — служебный блок версии данных (блок "dataversion");
    ///   Yields — блок доходности сделок (блок "trades_yields", может быть пустым).
    /// </summary>
    public sealed record RealtimeTradesParseResult<TTrade>(
        List<TTrade> Rows,
        RealtimeDataVersionDTO DataVersion,
        List<RealtimeTradesYieldsDTO> Yields);
}
