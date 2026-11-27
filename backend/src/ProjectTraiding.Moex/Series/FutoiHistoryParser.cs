using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class FutoiHistoryParser
{
    /// <summary>
    /// Читает одну строку открытого интереса. Код инструмента берётся из колонки ticker ответа,
    /// как в прежнем разборе. Запись выполняет вызывающий код.
    /// </summary>
    internal static bool TryReadRow(
        ref Utf8JsonReader reader, int columnCount,
        out FutoiRow row, out DateTime? sourceTime, out int columnIndex, out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? sessId, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? seqnum, out error)
            && ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? ticker, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? clgroup, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? pos, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? posLong, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? posShort, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? posLongNum, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? posShortNum, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? tradeSessionDate, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        if (date is not DateOnly tradeDate || time is not TimeOnly tradeTime)
        {
            error = "Строка открытого интереса отвергнута: дата или время пустые.";
            return false;
        }

        sourceTime = tradeDate.ToDateTime(tradeTime, DateTimeKind.Unspecified);
        if (!ParseHelpersUtf8.TryRowEnd(ref reader, ref columnIndex, columnCount, out error))
            return false;
        if (string.IsNullOrWhiteSpace(ticker))
        {
            error = "Строка открытого интереса отвергнута: ticker пустой.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(clgroup))
        {
            error = "Строка открытого интереса отвергнута: clgroup пустой.";
            return false;
        }
        if (!CandleDateTime64.TryToUnixWholeSeconds(sourceTime.Value, out ulong sourceUnixTime))
        {
            error = "[futoi] Не удалось преобразовать время строки в Unix-время.";
            return false;
        }

        row = new FutoiRow
        {
            Secid = ticker!,
            SourceTime = sourceUnixTime,
            Clgroup = clgroup!,
            SessId = sessId,
            Seqnum = seqnum,
            Pos = pos,
            PosLong = posLong,
            PosShort = posShort,
            PosLongNum = posLongNum,
            PosShortNum = posShortNum,
            TradeSessionDate = tradeSessionDate,
            Systime = systime,
        };
        return true;
    }
}
