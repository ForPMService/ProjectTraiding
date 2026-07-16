using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Contracts.Serialization;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Писатель последних принятых значений в оперативное хранилище для будущего живого графика.
    /// Единственный владелец этих ключей — контур Moex (приёмник). Пишем максимум состава:
    /// последнюю сделку и последний снимок стакана целиком — что понадобится графику, ещё неясно.
    ///
    /// Redis здесь витрина последних значений, НЕ источник истины приёма: истину несут
    /// ClickHouse (данные) и moex_stream_cursors (отметка). Сбой записи не роняет приём:
    /// пишется в журнал и проглатывается, как в LoadProgressWriter.
    /// </summary>
    public sealed class RealtimeLatestWriter
    {
        private const string StockTradeKeyPrefix = "moex:latest:trade:stock:v1:";
        private const string FuturesTradeKeyPrefix = "moex:latest:trade:futures:v1:";
        private const string OrderbookKeyPrefix = "moex:latest:orderbook:v1:";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RealtimeLatestWriter> _logger;

        public RealtimeLatestWriter(IConnectionMultiplexer redis, ILogger<RealtimeLatestWriter> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task WriteLatestStockTradeAsync(
            string secid, RealtimeTradesStockDTO trade, CancellationToken ct)
        {
            await WriteJsonAsync(
                StockTradeKeyPrefix + secid,
                JsonSerializer.Serialize(trade, AppJsonContext.Default.RealtimeTradesStockDTO),
                secid);
        }

        public async Task WriteLatestFuturesTradeAsync(
            string secid, RealtimeTradesFuturesDTO trade, CancellationToken ct)
        {
            await WriteJsonAsync(
                FuturesTradeKeyPrefix + secid,
                JsonSerializer.Serialize(trade, AppJsonContext.Default.RealtimeTradesFuturesDTO),
                secid);
        }

        public async Task WriteLatestOrderbookAsync(
            string secid, List<RealtimeOrderbookRowDTO> snapshot, CancellationToken ct)
        {
            await WriteJsonAsync(
                OrderbookKeyPrefix + secid,
                JsonSerializer.Serialize(snapshot, AppJsonContext.Default.ListRealtimeOrderbookRowDTO),
                secid);
        }

        private async Task WriteJsonAsync(string key, string json, string secid)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                await db.StringSetAsync(key, json);
                MoexRealtimeLatestLogMessages.LatestWritten(_logger, key);
            }
            catch (Exception ex)
            {
                MoexRealtimeLatestLogMessages.LatestWriteFailed(
                    _logger, ex, key, ex.GetType().Name);
            }
        }
    }
}
