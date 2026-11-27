using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class ObStatsHistoryParser
{
    /// <summary>
    /// Читает одну строку статистики стакана. Код инструмента берётся из задания:
    /// колонка secid ответа только проверяется. Запись выполняет вызывающий код.
    /// </summary>
    internal static bool TryReadStockRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out ObStatsStockRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextStringToken(ref reader, ref columnIndex, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadBbo, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadLv10, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spread1Mio, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? levelsB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? levelsS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? valB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? valS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? imbalanceVolBbo, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? imbalanceValBbo, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? imbalanceVol, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? imbalanceVal, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapB1Mio, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapS1Mio, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        if (!ParseHelpersUtf8.TryRowEnd(ref reader, ref columnIndex, columnCount, out error))
            return false;
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[obstats] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new ObStatsStockRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            SpreadBbo = spreadBbo,
            SpreadLv10 = spreadLv10,
            Spread1Mio = spread1Mio,
            LevelsB = levelsB,
            LevelsS = levelsS,
            VolB = volB,
            VolS = volS,
            ValB = valB,
            ValS = valS,
            ImbalanceVolBbo = imbalanceVolBbo,
            ImbalanceValBbo = imbalanceValBbo,
            ImbalanceVol = imbalanceVol,
            ImbalanceVal = imbalanceVal,
            VwapB = vwapB,
            VwapS = vwapS,
            VwapB1Mio = vwapB1Mio,
            VwapS1Mio = vwapS1Mio,
            Systime = systime,
        };
        return true;
    }

    internal static bool TryReadFuturesRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out ObStatsFuturesRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextStringToken(ref reader, ref columnIndex, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? assetCode, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? midPrice, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? microPrice, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadL1, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadL2, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadL3, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadL5, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadL10, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? spreadL20, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? levelsB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? levelsS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volBL1, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volBL2, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volBL3, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volBL5, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volBL10, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volBL20, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volSL1, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volSL2, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volSL3, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volSL5, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volSL10, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volSL20, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapBL3, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapBL5, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapBL10, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapBL20, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapSL3, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapSL5, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapSL10, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? vwapSL20, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        if (!ParseHelpersUtf8.TryRowEnd(ref reader, ref columnIndex, columnCount, out error))
            return false;
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[obstats] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new ObStatsFuturesRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            AssetCode = assetCode,
            MidPrice = midPrice,
            MicroPrice = microPrice,
            SpreadL1 = spreadL1,
            SpreadL2 = spreadL2,
            SpreadL3 = spreadL3,
            SpreadL5 = spreadL5,
            SpreadL10 = spreadL10,
            SpreadL20 = spreadL20,
            LevelsB = levelsB,
            LevelsS = levelsS,
            VolBL1 = volBL1,
            VolBL2 = volBL2,
            VolBL3 = volBL3,
            VolBL5 = volBL5,
            VolBL10 = volBL10,
            VolBL20 = volBL20,
            VolSL1 = volSL1,
            VolSL2 = volSL2,
            VolSL3 = volSL3,
            VolSL5 = volSL5,
            VolSL10 = volSL10,
            VolSL20 = volSL20,
            VwapBL3 = vwapBL3,
            VwapBL5 = vwapBL5,
            VwapBL10 = vwapBL10,
            VwapBL20 = vwapBL20,
            VwapSL3 = vwapSL3,
            VwapSL5 = vwapSL5,
            VwapSL10 = vwapSL10,
            VwapSL20 = vwapSL20,
            Systime = systime,
        };
        return true;
    }
}
