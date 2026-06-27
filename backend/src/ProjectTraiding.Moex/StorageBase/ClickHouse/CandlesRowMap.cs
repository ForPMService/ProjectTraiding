using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Единственный источник истины для вставки свечей в moex_candles_1m:
    /// порядок столбцов (V012), типы столбцов, чистое преобразование DTO в строку
    /// по позициям и стражи ключевых полей. Без базы, настроек и часов.
    /// </summary>
    public static class CandlesRowMap
    {
        public const string Table = "moex_candles_1m";

        /// <summary>Порядок столбцов строго по V012. Длина 9. Изменение порядка = изменение контракта.</summary>
        public static readonly string[] Columns =
        {
            "secid", "open", "close", "high", "low", "value", "volume", "begin", "end"
        };

        /// <summary>
        /// Типы столбцов (V012) для InsertOptions.ColumnTypes — чтобы исполнитель не слал
        /// предварительный запрос схемы. Сопоставление по имени; состав совпадает с Columns.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ColumnTypes =
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

        /// <summary>
        /// secid — параметр диапазона, один на весь батч (в ответе свечей его нет).
        /// Проверяется один раз писателем до цикла, а не на каждой свече.
        /// </summary>
        public static void EnsureSecid(string secid)
        {
            if (string.IsNullOrWhiteSpace(secid))
                throw new InvalidOperationException("Загрузка свечей отвергнута: secid пустой.");
        }

        /// <summary>
        /// begin — ключевой столбец (not-null, ORDER BY), приходит из ответа у каждой свечи свой,
        /// поэтому проверяется построчно. Остальные семь полей пустыми допустимы.
        /// </summary>
        public static void EnsureWritable(CandlesDTO candle)
        {
            if (candle.Begin is null)
                throw new InvalidOperationException("Свеча отвергнута: begin пустой.");
        }

        /// <summary>
        /// Свеча и уже проверенный secid в строку вставки строго по Columns.
        /// Вызывает построчный страж begin; secid здесь не перепроверяется (проверен EnsureSecid).
        /// begin/end приводятся к Kind=Unspecified — московское стенное время без сдвига зоны.
        /// null в неключевых полях не подменяется нулём.
        /// </summary>
        public static object?[] ToRow(CandlesDTO candle, string secid)
        {
            EnsureWritable(candle);

            return new object?[]
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
        }

        private static DateTime? AsWallClock(DateTime? value)
        {
            if (value is null)
                return null;

            return DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        }
    }
}
