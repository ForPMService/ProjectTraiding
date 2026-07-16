using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки свечей: порядок столбцов (V012), типы, превращение свечи в строку по
    /// позициям и страж ключевого begin. Форма свечи от интервала не зависит, поэтому одна карта
    /// обслуживает все интервалы — различаются лишь целевая таблица и префикс токена, заданные
    /// в конструкторе (moex_candles_1m + "candles:1m", moex_candles_10m + "candles:10m" и т.д.).
    /// </summary>
    public sealed class CandlesRowMap : IRowMap<CandlesDTO>
    {
        public string Table { get; }

        public string TokenPrefix { get; }

        // Версия схлопывания ReplacingMergeTree: 1 у исторической загрузки (перекрывает),
        // 0 у приёмника реального времени. При равном ключе (secid, begin) побеждает бо́льшая.
        private readonly byte _ingestPriority;

        public CandlesRowMap(string table, string tokenPrefix, byte ingestPriority)
        {
            if (string.IsNullOrWhiteSpace(table))
                throw new ArgumentException("Имя таблицы свечей обязательно.", nameof(table));
            if (string.IsNullOrWhiteSpace(tokenPrefix))
                throw new ArgumentException("Префикс токена свечей обязателен.", nameof(tokenPrefix));

            Table = table;
            TokenPrefix = tokenPrefix;
            _ingestPriority = ingestPriority;
        }

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "open", "close", "high", "low", "value", "volume", "begin", "end",
            "ingest_priority"
        };

        public IReadOnlyDictionary<string, string> ColumnTypes { get; } =
            new Dictionary<string, string>
            {
                ["secid"] = "LowCardinality(String)",
                ["open"] = "Nullable(Float64)",
                ["close"] = "Nullable(Float64)",
                ["high"] = "Nullable(Float64)",
                ["low"] = "Nullable(Float64)",
                ["value"] = "Nullable(Float64)",
                ["volume"] = "Nullable(Float64)",
                ["begin"] = "DateTime64(3, 'Europe/Moscow')",
                ["end"] = "Nullable(DateTime64(3, 'Europe/Moscow'))",
                ["ingest_priority"] = "UInt8",
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка свечей отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(
            CandlesDTO candle, string secid, string? tradeSessionDate)
        {
            if (candle.Begin is null)
                throw new InvalidOperationException("Свеча отвергнута: begin пустой.");

            DateTime begin = candle.Begin.Value;

            object?[] row =
            {
                secid,
                candle.Open,
                candle.Close,
                candle.High,
                candle.Low,
                candle.Value,
                candle.Volume,
                MoexClickHouseTime.AsWallClock(candle.Begin),
                MoexClickHouseTime.AsWallClock(candle.End),
                _ingestPriority
            };

            return (row, DateTime.SpecifyKind(begin, DateTimeKind.Unspecified));
        }
    }
}
