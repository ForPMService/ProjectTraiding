using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>
/// Разбирает одну строку TradeStats. На входе reader стоит на StartArray строки;
/// проверка входных колонок и начала строки выполняется вызывающим кодом.
/// При успехе reader стоит на EndArray, при отказе — на проблемном токене.
/// columnIndex — число предпринятых чтений (индекс ошибочной колонки: columnIndex - 1).
/// SourceTime структуры не заполняется: разобранное московское время возвращается отдельно.
/// </summary>
public static class TradeStatsHistoryParser
{
    private const string RootKey = "tradestats";

    internal static bool TryReadStockRow(
        ref Utf8JsonReader reader,
        out TradeStatsStockRow row,
        out DateTime? sourceTime,
        out int columnIndex,
        out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? secid, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prOpen, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prHigh, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prLow, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prClose, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prStd, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? vol, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? val, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? trades, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwap, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prChange, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? disb, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapS, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrOpen, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrHigh, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrLow, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrClose, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.EndArray, "строка Stock", RootKey);
        row = new TradeStatsStockRow
        {
            Secid = secid!,
            PrOpen = prOpen,
            PrHigh = prHigh,
            PrLow = prLow,
            PrClose = prClose,
            PrStd = prStd,
            Vol = vol,
            Val = val,
            Trades = trades,
            PrVwap = prVwap,
            PrChange = prChange,
            TradesB = tradesB,
            TradesS = tradesS,
            ValB = valB,
            ValS = valS,
            VolB = volB,
            VolS = volS,
            Disb = disb,
            PrVwapB = prVwapB,
            PrVwapS = prVwapS,
            Systime = systime,
            SecPrOpen = secPrOpen,
            SecPrHigh = secPrHigh,
            SecPrLow = secPrLow,
            SecPrClose = secPrClose,
        };
        return true;
    }

    internal static bool TryReadFuturesRow(
        ref Utf8JsonReader reader,
        out TradeStatsFuturesRow row,
        out DateTime? sourceTime,
        out int columnIndex,
        out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? secid, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? assetCode, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prOpen, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prHigh, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prLow, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prClose, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prStd, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? vol, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? val, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? trades, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwap, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prChange, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? disb, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? im, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiOpen, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiHigh, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiLow, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiClose, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrOpen, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrHigh, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrLow, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrClose, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.EndArray, "строка Futures", RootKey);
        row = new TradeStatsFuturesRow
        {
            Secid = secid!,
            AssetCode = assetCode,
            PrOpen = prOpen,
            PrHigh = prHigh,
            PrLow = prLow,
            PrClose = prClose,
            PrStd = prStd,
            Vol = vol,
            Val = val,
            Trades = trades,
            PrVwap = prVwap,
            PrChange = prChange,
            TradesB = tradesB,
            TradesS = tradesS,
            ValB = valB,
            ValS = valS,
            VolB = volB,
            VolS = volS,
            Disb = disb,
            PrVwapB = prVwapB,
            PrVwapS = prVwapS,
            Im = im,
            OiOpen = oiOpen,
            OiHigh = oiHigh,
            OiLow = oiLow,
            OiClose = oiClose,
            SecPrOpen = secPrOpen,
            SecPrHigh = secPrHigh,
            SecPrLow = secPrLow,
            SecPrClose = secPrClose,
            Systime = systime,
        };
        return true;
    }
}
