using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class MegaAlertsHistoryParser
{
    internal static bool TryReadStockRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out MegaAlertsStockRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
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
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? alertType, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? threshold, out error)
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
        if (string.IsNullOrWhiteSpace(alertType))
        {
            error = "Строка мегаалёртов отвергнута: alert_type пустой.";
            return false;
        }
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[mega_alerts] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new MegaAlertsStockRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            AlertType = alertType!,
            Threshold = threshold,
            Value = value,
            Reference = reference,
            Systime = systime,
        };
        return true;
    }

    internal static bool TryReadFuturesRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out MegaAlertsFuturesRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
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
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? alertType, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? threshold, out error)
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
        if (string.IsNullOrWhiteSpace(alertType))
        {
            error = "Строка мегаалёртов отвергнута: alert_type пустой.";
            return false;
        }
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[mega_alerts] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new MegaAlertsFuturesRow
        {
            Secid = secId,
            SourceTime = sourceUnixTime,
            AssetCode = assetCode,
            AlertType = alertType!,
            Threshold = threshold,
            Value = value,
            Reference = reference,
            Systime = systime,
        };
        return true;
    }
}
