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

        public CandlesRowMap(string table, string tokenPrefix)
        {
            if (string.IsNullOrWhiteSpace(table))
                throw new ArgumentException("Имя таблицы свечей обязательно.", nameof(table));
            if (string.IsNullOrWhiteSpace(tokenPrefix))
                throw new ArgumentException("Префикс токена свечей обязателен.", nameof(tokenPrefix));

            Table = table;
            TokenPrefix = tokenPrefix;
        }

        public IReadOnlyList<string> Columns { get; } = new[]
        {
            "secid", "open", "close", "high", "low", "value", "volume", "begin", "end"
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
            };

        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка свечей отвергнута: secid пустой.");
        }

        public (object?[] Row, DateTime Time) ToRow(CandlesDTO candle, string secid)
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
                AsWallClock(candle.Begin),
                AsWallClock(candle.End)
            };

            return (row, DateTime.SpecifyKind(begin, DateTimeKind.Unspecified));
        }

        private static DateTime? AsWallClock(DateTime? value)
        {
            if (value is null)
                return null;

            return DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        }
    }
}