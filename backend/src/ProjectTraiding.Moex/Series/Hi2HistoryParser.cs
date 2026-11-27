using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class Hi2HistoryParser
{
    internal static bool TryReadStockRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out Hi2StockRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
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
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? metric, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? value, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? reference, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        if (!ParseHelpersUtf8.TryRowEnd(ref reader, ref columnIndex, columnCount, out error))
            return false;
        if (string.IsNullOrWhiteSpace(metric))
        {
            error = "Строка концентрации отвергнута: metric пустой.";
            return false;
        }
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[hi2] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new Hi2StockRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            Metric = metric!,
            Value = value,
            Reference = reference,
            Systime = systime,
        };
        return true;
    }

    internal static bool TryReadFuturesRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out Hi2FuturesRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
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
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? metric, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? value, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? reference, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        if (!ParseHelpersUtf8.TryRowEnd(ref reader, ref columnIndex, columnCount, out error))
            return false;
        if (string.IsNullOrWhiteSpace(metric))
        {
            error = "Строка концентрации отвергнута: metric пустой.";
            return false;
        }
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[hi2] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new Hi2FuturesRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            AssetCode = assetCode,
            Metric = metric!,
            Value = value,
            Reference = reference,
            Systime = systime,
        };
        return true;
    }
}
