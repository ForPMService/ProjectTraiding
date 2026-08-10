using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using ProjectTraiding.Moex.Contracts.Serialization;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Писатель последних принятых значений в оперативное хранилище для будущего живого графика.
    /// Единственный владелец этих ключей — контур Moex (приёмник). Пишем максимум состава:
    /// последнюю сделку и последний снимок стакана целиком — что понадобится графику, ещё неясно.
    ///
    /// Redis здесь витрина последних значений, НЕ источник истины приёма: истину несут
    /// ClickHouse (данные) и moex_stream_cursors (отметка). Сбой записи не роняет приём:
    /// пишется в журнал и проглатывается.
    /// </summary>
    public sealed class RealtimeLatestWriter
    {
        private const string StockTradeKeyPrefix = "moex:latest:trade:stock:v1:";
        private const string FuturesTradeKeyPrefix = "moex:latest:trade:futures:v1:";
        private const string OrderbookKeyPrefix = "moex:latest:orderbook:v1:";
        private const string CandleKeyPrefix = "moex:latest:candle:1m:v1:";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RealtimeLatestWriter> _logger;

        public RealtimeLatestWriter(IConnectionMultiplexer redis, ILogger<RealtimeLatestWriter> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public Task WriteLatestStockTradeAsync(
            string secid, RealtimeTradesStockDTO trade, CancellationToken ct)
        {
            return WriteLatestAsync(
                StockTradeKeyPrefix + secid,
                trade,
                AppJsonContext.Default.RealtimeTradesStockDTO);
        }

        public Task WriteLatestFuturesTradeAsync(
            string secid, RealtimeTradesFuturesDTO trade, CancellationToken ct)
        {
            return WriteLatestAsync(
                FuturesTradeKeyPrefix + secid,
                trade,
                AppJsonContext.Default.RealtimeTradesFuturesDTO);
        }

        public Task WriteLatestOrderbookAsync(
            string secid, List<RealtimeOrderbookRowDTO> snapshot, CancellationToken ct)
        {
            return WriteLatestAsync(
                OrderbookKeyPrefix + secid,
                snapshot,
                AppJsonContext.Default.ListRealtimeOrderbookRowDTO);
        }

        // Ключ — последняя известная свеча ответа: пока минута растёт, это её текущий снимок;
        // когда минута закрылась, а следующая ещё не началась, это её же окончательная версия из
        // ответа. Служба пишет сюда последнюю по Begin свечу каждого ответа, закрытую или растущую.
        // Срока жизни и удаления нет (как у сделок и стакана); потребитель различает минуты по Begin.
        public Task WriteLatestCandleAsync(
            string secid, CandlesDTO candle, CancellationToken ct)
        {
            return WriteLatestAsync(
                CandleKeyPrefix + secid,
                candle,
                AppJsonContext.Default.CandlesDTO);
        }

        // Сериализация внутри перехвата — так контракт «сбой записи не роняет приём» держится
        // и для сбоя сериализации, а не только сетевой записи. Прежняя версия сериализовала в
        // аргументе WriteJsonAsync, вне try, и сбой сериализации проходил мимо перехвата.
        // JsonTypeInfo<T> берётся из генератора кода (AppJsonContext) — без рефлексии, пригодно
        // для нативной AOT-компиляции.
        private async Task WriteLatestAsync<T>(string key, T value, JsonTypeInfo<T> typeInfo)
        {
            try
            {
                string json = JsonSerializer.Serialize(value, typeInfo);
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
