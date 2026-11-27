using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class OrderStatsHistoryParser
{
    internal static bool TryReadRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out OrderStatsStockRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
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
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? putOrdersB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? putOrdersS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? putValB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? putValS, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? putVolB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? putVolS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? putVwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? putVwapS, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? putVol, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? putVal, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? putOrders, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? cancelOrdersB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? cancelOrdersS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? cancelValB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? cancelValS, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? cancelVolB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? cancelVolS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? cancelVwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? cancelVwapS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? cancelVol, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? cancelVal, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? cancelOrders, out error)
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
            error = "[orderstats] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new OrderStatsStockRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            PutOrdersB = putOrdersB,
            PutOrdersS = putOrdersS,
            PutValB = putValB,
            PutValS = putValS,
            PutVolB = putVolB,
            PutVolS = putVolS,
            PutVwapB = putVwapB,
            PutVwapS = putVwapS,
            PutVol = putVol,
            PutVal = putVal,
            PutOrders = putOrders,
            CancelOrdersB = cancelOrdersB,
            CancelOrdersS = cancelOrdersS,
            CancelValB = cancelValB,
            CancelValS = cancelValS,
            CancelVolB = cancelVolB,
            CancelVolS = cancelVolS,
            CancelVwapB = cancelVwapB,
            CancelVwapS = cancelVwapS,
            CancelVol = cancelVol,
            CancelVal = cancelVal,
            CancelOrders = cancelOrders,
            Systime = systime,
        };
        return true;
    }
}
