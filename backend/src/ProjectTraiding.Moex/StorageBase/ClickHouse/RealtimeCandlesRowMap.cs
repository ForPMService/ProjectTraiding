using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Series;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Свечная карта приёмника реального времени. Таблица, префикс токена, имена и
    /// типы столбцов берутся из декларации минутных свечей — единственного описания
    /// формы; здесь остаются только сборка строки из объекта передачи данных (до
    /// перевода разбора приёма на общий разборщик на этапе C) и приоритет вставки 0:
    /// историческая загрузка (приоритет 1) перекрывает при слиянии по ключу
    /// (secid, begin).
    /// </summary>
    public sealed class RealtimeCandlesRowMap : IRowMap<CandlesDTO>
    {
        private const byte IngestPriority = 0;

        private static readonly MoexSeriesSpec Spec = MoexSeriesRegistry.CandlesStock1m;

        public string Table => Spec.Table;

        public string TokenPrefix => Spec.TokenPrefix;

        public IReadOnlyList<string> Columns { get; }

        public IReadOnlyDictionary<string, string> ColumnTypes { get; }

        public RealtimeCandlesRowMap()
        {
            string[] columns = new string[Spec.TargetColumns.Length];
            Dictionary<string, string> columnTypes = new(Spec.TargetColumns.Length);
            for (int i = 0; i < Spec.TargetColumns.Length; i++)
            {
                columns[i] = Spec.TargetColumns[i].Name;
                columnTypes[columns[i]] = Spec.TargetColumns[i].ColumnType;
            }

            Columns = columns;
            ColumnTypes = columnTypes;
        }

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
                IngestPriority
            };

            return (row, DateTime.SpecifyKind(begin, DateTimeKind.Unspecified));
        }
    }
}
