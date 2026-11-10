using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>Parses stock and futures tradestats directly into their Protobuf row types.</summary>
public static class TradeStatsHistoryParser
{
    private delegate void RowReader<TRow>(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        List<(TRow Row, DateTime Time)> rows,
        ref int sourceRowIndex,
        ref List<SkippedSourceRow>? skippedSourceRows)
        where TRow : struct;

    public static TradeStatsParsedPage<TradeStatsStockRow> ParseStock(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "stock");
        return Parse<TradeStatsStockRow>(body, spec, taskSecId, ReadStockRows, out cursor);
    }

    public static TradeStatsParsedPage<TradeStatsFuturesRow> ParseFutures(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "futures");
        return Parse<TradeStatsFuturesRow>(body, spec, taskSecId, ReadFuturesRows, out cursor);
    }

    private static TradeStatsParsedPage<TRow> Parse<TRow>(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        RowReader<TRow> readRows,
        out PaginationCursorDTO cursor)
        where TRow : struct
    {
        List<(TRow Row, DateTime Time)> rows = [];
        int sourceRowsCount = 0;
        List<SkippedSourceRow>? skippedSourceRows = null;
        Utf8JsonReader reader = new(body);

        ParseHelpersUtf8.SkipToRootObject(ref reader, spec.RootKey);
        _ = spec.SourceColumnKindsValid;

        bool foundColumns = false;
        bool foundData = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("columns"u8))
            {
                foundColumns = true;
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, spec.SourceColumns, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }

                foundData = true;
                readRows(
                    ref reader,
                    spec,
                    taskSecId,
                    rows,
                    ref sourceRowsCount,
                    ref skippedSourceRows);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);

        cursor = new PaginationCursorDTO();
        if (spec.Pagination == PaginationKind.Cursor)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("data.cursor"u8))
                {
                    try
                    {
                        cursor = ParseHelpersUtf8.ReadCursorRootObject(ref reader);
                    }
                    catch (InvalidOperationException ex)
                    {
                        ParseHelpersUtf8.SchemaMismatch(ex.Message);
                    }

                    break;
                }

                reader.Skip();
            }
        }

        return new TradeStatsParsedPage<TRow>(rows, sourceRowsCount, skippedSourceRows);
    }

    private static void ReadStockRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        List<(TradeStatsStockRow Row, DateTime Time)> rows,
        ref int sourceRowIndex,
        ref List<SkippedSourceRow>? skippedSourceRows)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        SourceTimeParts sourceTimeParts = default;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался StartArray строки {sourceRowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            sourceTimeParts.Reset();
            TradeStatsStockRow row = new() { Secid = taskSecId };
            string? rowError = null;

            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Неожиданный конец JSON в строке {sourceRowIndex}, " +
                        $"позиция {position}.");
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Короткая строка данных: ожидалось " +
                        $"{spec.SourceColumns.Length} колонок, получено {position} " +
                        $"(строка {sourceRowIndex}).");
                }

                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                try
                {
                    switch (position)
                    {
                        case 0:
                            ReadMomentDate(ref reader, spec, sourceRowIndex, ref sourceTimeParts);
                            break;
                        case 1:
                            ReadMomentTime(ref reader, spec, sourceRowIndex, ref sourceTimeParts);
                            break;
                        case 2:
                            ParseHelpersUtf8.ExpectString(ref reader, sourceRowIndex, position, spec.RootKey);
                            break;
                        case 3: row.PrOpen = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 4: row.PrHigh = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 5: row.PrLow = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 6: row.PrClose = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 7: row.PrStd = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 8: row.Vol = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 9: row.Val = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 10: row.Trades = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 11: row.PrVwap = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 12: row.PrChange = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 13: row.TradesB = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 14: row.TradesS = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 15: row.ValB = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 16: row.ValS = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 17: row.VolB = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 18: row.VolS = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 19: row.Disb = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 20: row.PrVwapB = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 21: row.PrVwapS = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 22:
                            row.Systime = CandleDateTime64.ToUnixSeconds(
                                ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, sourceRowIndex, position, spec.RootKey));
                            break;
                        case 23: row.SecPrOpen = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 24: row.SecPrHigh = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 25: row.SecPrLow = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 26: row.SecPrClose = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        default:
                            ParseHelpersUtf8.SchemaMismatch(
                                $"[{spec.RootKey}] Неизвестная колонка источника на позиции {position}.");
                            break;
                    }
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or FormatException or ArgumentException)
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{spec.RootKey}] Строка {sourceRowIndex} завершилась на колонке {position}.");
                    }

                    rowError ??= ex.Message;
                    reader.Skip();
                }
            }

            FinishStockRow(
                ref reader, spec, taskSecId, rows, sourceRowIndex,
                ref sourceTimeParts, ref skippedSourceRows, row, rowError);
            sourceRowIndex++;
        }
    }

    private static void ReadFuturesRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        List<(TradeStatsFuturesRow Row, DateTime Time)> rows,
        ref int sourceRowIndex,
        ref List<SkippedSourceRow>? skippedSourceRows)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", spec.RootKey);

        SourceTimeParts sourceTimeParts = default;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{spec.RootKey}] Ожидался StartArray строки {sourceRowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            sourceTimeParts.Reset();
            TradeStatsFuturesRow row = new() { Secid = taskSecId };
            string? rowError = null;

            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Неожиданный конец JSON в строке {sourceRowIndex}, " +
                        $"позиция {position}.");
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Короткая строка данных: ожидалось " +
                        $"{spec.SourceColumns.Length} колонок, получено {position} " +
                        $"(строка {sourceRowIndex}).");
                }

                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                try
                {
                    switch (position)
                    {
                        case 0:
                            ReadMomentDate(ref reader, spec, sourceRowIndex, ref sourceTimeParts);
                            break;
                        case 1:
                            ReadMomentTime(ref reader, spec, sourceRowIndex, ref sourceTimeParts);
                            break;
                        case 2:
                            ParseHelpersUtf8.ExpectString(ref reader, sourceRowIndex, position, spec.RootKey);
                            break;
                        case 3: row.AssetCode = ParseHelpersUtf8.ReadString(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 4: row.PrOpen = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 5: row.PrHigh = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 6: row.PrLow = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 7: row.PrClose = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 8: row.PrStd = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 9: row.Vol = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 10: row.Val = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 11: row.Trades = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 12: row.PrVwap = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 13: row.PrChange = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 14: row.TradesB = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 15: row.TradesS = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 16: row.ValB = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 17: row.ValS = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 18: row.VolB = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 19: row.VolS = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 20: row.Disb = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 21: row.PrVwapB = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 22: row.PrVwapS = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 23: row.Im = ParseHelpersUtf8.ReadDouble(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 24: row.OiOpen = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 25: row.OiHigh = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 26: row.OiLow = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 27: row.OiClose = ParseHelpersUtf8.ReadLong(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 28: row.SecPrOpen = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 29: row.SecPrHigh = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 30: row.SecPrLow = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 31: row.SecPrClose = ParseHelpersUtf8.ReadInt(ref reader, sourceRowIndex, position, spec.RootKey); break;
                        case 32:
                            row.Systime = CandleDateTime64.ToUnixSeconds(
                                ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, sourceRowIndex, position, spec.RootKey));
                            break;
                        default:
                            ParseHelpersUtf8.SchemaMismatch(
                                $"[{spec.RootKey}] Неизвестная колонка источника на позиции {position}.");
                            break;
                    }
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException or FormatException or ArgumentException)
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{spec.RootKey}] Строка {sourceRowIndex} завершилась на колонке {position}.");
                    }

                    rowError ??= ex.Message;
                    reader.Skip();
                }
            }

            FinishFuturesRow(
                ref reader, spec, taskSecId, rows, sourceRowIndex,
                ref sourceTimeParts, ref skippedSourceRows, row, rowError);
            sourceRowIndex++;
        }
    }

    private static void FinishStockRow(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        List<(TradeStatsStockRow Row, DateTime Time)> rows,
        int sourceRowIndex,
        ref SourceTimeParts sourceTimeParts,
        ref List<SkippedSourceRow>? skippedSourceRows,
        TradeStatsStockRow row,
        string? rowError)
    {
        FinishRowArray(ref reader, spec, sourceRowIndex);
        DateTime? sourceTime = TryReadSourceTime(in sourceTimeParts);

        try
        {
            if (rowError is not null)
                throw new InvalidOperationException(rowError);
            ValidateSecId(taskSecId, spec);
            DateTime requiredSourceTime = MoexClickHouseTime.BuildSourceTime(in sourceTimeParts);
            row.SourceTime = CandleDateTime64.ToUnixSeconds(requiredSourceTime);
            rows.Add((row, requiredSourceTime));
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or FormatException or ArgumentException)
        {
            skippedSourceRows ??= new List<SkippedSourceRow>();
            skippedSourceRows.Add(new SkippedSourceRow(
                sourceRowIndex, sourceTime, rowError ?? ex.Message));
        }
    }

    private static void FinishFuturesRow(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        List<(TradeStatsFuturesRow Row, DateTime Time)> rows,
        int sourceRowIndex,
        ref SourceTimeParts sourceTimeParts,
        ref List<SkippedSourceRow>? skippedSourceRows,
        TradeStatsFuturesRow row,
        string? rowError)
    {
        FinishRowArray(ref reader, spec, sourceRowIndex);
        DateTime? sourceTime = TryReadSourceTime(in sourceTimeParts);

        try
        {
            if (rowError is not null)
                throw new InvalidOperationException(rowError);
            ValidateSecId(taskSecId, spec);
            DateTime requiredSourceTime = MoexClickHouseTime.BuildSourceTime(in sourceTimeParts);
            row.SourceTime = CandleDateTime64.ToUnixSeconds(requiredSourceTime);
            rows.Add((row, requiredSourceTime));
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or FormatException or ArgumentException)
        {
            skippedSourceRows ??= new List<SkippedSourceRow>();
            skippedSourceRows.Add(new SkippedSourceRow(
                sourceRowIndex, sourceTime, rowError ?? ex.Message));
        }
    }

    private static void FinishRowArray(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        int sourceRowIndex)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{spec.RootKey}] Ожидался EndArray после " +
                $"{spec.SourceColumns.Length} колонок (строка {sourceRowIndex}).");
        }
    }

    private static void ReadMomentDate(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        int sourceRowIndex,
        ref SourceTimeParts sourceTimeParts)
    {
        sourceTimeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(
            ref reader, sourceRowIndex, 0, spec.RootKey, out sourceTimeParts.RawDate);
    }

    private static void ReadMomentTime(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        int sourceRowIndex,
        ref SourceTimeParts sourceTimeParts)
    {
        sourceTimeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(
            ref reader, sourceRowIndex, 1, spec.RootKey, out sourceTimeParts.RawTime);
    }

    private static DateTime? TryReadSourceTime(in SourceTimeParts sourceTimeParts)
    {
        try
        {
            return MoexClickHouseTime.BuildSourceTime(in sourceTimeParts);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static void ValidateSecId(string taskSecId, MoexSeriesSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(taskSecId))
            return;

        TargetColumn secIdColumn = spec.TargetColumns[0];
        throw new InvalidOperationException(
            secIdColumn.RequiredMessage
            ?? $"Строка отвергнута: обязательная колонка {secIdColumn.Name} пуста.");
    }

    private static void ValidateSpec(MoexSeriesSpec spec, string expectedMarket)
    {
        if (spec.DataKind != "tradestats"
            || spec.Market != expectedMarket
            || spec.Pagination != PaginationKind.Cursor)
        {
            throw new ArgumentException(
                $"Typed trade statistics parsing requires {expectedMarket} tradestats with cursor pagination.",
                nameof(spec));
        }
    }
}
