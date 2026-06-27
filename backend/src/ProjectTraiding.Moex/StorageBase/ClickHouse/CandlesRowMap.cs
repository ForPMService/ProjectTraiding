using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Карта формы вставки свечей в moex_candles_1m: порядок столбцов (V012), типы, превращение
    /// свечи в строку по позициям и стражи ключевых полей. Реализует общий контракт карты,
    /// поэтому писатель работает с ней, не зная, что это именно свечи.
    /// </summary>
    public sealed class CandlesRowMap : IRowMap<CandlesDTO>
    {
        public string Table => "moex_candles_1m";

        public string TokenPrefix => "candles:1m";

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

        // secid — параметр диапазона, один на весь батч (в ответе свечей его нет). Один раз до цикла.
        public void EnsureRangeValid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка свечей отвергнута: secid пустой.");
        }

        // begin — ключевой not-null столбец (ORDER BY), у каждой свечи свой → проверяется построчно.
        // Остальные семь полей пустыми допустимы. begin/end приводятся к Kind=Unspecified —
        // московское стенное время без сдвига зоны. null в неключевых полях не подменяется нулём.
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
