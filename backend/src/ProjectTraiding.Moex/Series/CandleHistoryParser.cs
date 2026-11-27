using System.Text.Json;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class CandleHistoryParser
{
    internal static bool TryReadRow(
        ref Utf8JsonReader reader, int columnCount, string secId,
        out CandleRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateTime begin = default;
        DateTime end = default;

        if (!(ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? open, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? close, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? high, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? low, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? value, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? volume, out error)
            && ParseHelpersUtf8.TryNextDateTime(ref reader, ref columnIndex, out begin, out error)
            && ParseHelpersUtf8.TryNextDateTime(ref reader, ref columnIndex, out end, out error)))
        {
            if (begin != default)
                sourceTime = begin;
            return false;
        }

        sourceTime = begin;
        if (!ParseHelpersUtf8.TryRowEnd(ref reader, ref columnIndex, columnCount, out error))
            return false;
        if (!CandleDateTime64.TryToUnixWholeSeconds(begin, out ulong beginUnixTime))
        {
            error = "Свеча отвергнута: begin не является допустимым временем.";
            return false;
        }
        if (!CandleDateTime64.TryToUnixWholeSeconds(end, out ulong endUnixTime))
        {
            error = "Свеча отвергнута: end не является допустимым временем.";
            return false;
        }

        row = new CandleRow
        {
            Secid = secId,
            Open = open,
            Close = close,
            High = high,
            Low = low,
            Value = value,
            Volume = volume,
            Begin = beginUnixTime,
            End = endUnixTime,
        };
        return true;
    }
}
