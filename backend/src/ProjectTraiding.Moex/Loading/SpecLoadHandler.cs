using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.ExceptionServices;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Loading;

public sealed class SpecLoadHandler
{
    private static readonly MoexSeriesSpec[] MigratedSpecs =
    [
        MoexSeriesRegistry.Hi2Stock, MoexSeriesRegistry.Hi2Futures,
        MoexSeriesRegistry.TradeStatsStock, MoexSeriesRegistry.TradeStatsFutures,
        MoexSeriesRegistry.ObStatsStock, MoexSeriesRegistry.ObStatsFutures,
        MoexSeriesRegistry.OrderStatsStock, MoexSeriesRegistry.MegaAlertsStock,
        MoexSeriesRegistry.MegaAlertsFutures, MoexSeriesRegistry.Futoi,
        MoexSeriesRegistry.CandlesStock1m, MoexSeriesRegistry.CandlesStock10m,
        MoexSeriesRegistry.CandlesStock1h, MoexSeriesRegistry.CandlesStock1d,
        MoexSeriesRegistry.CandlesFutures1m, MoexSeriesRegistry.CandlesFutures10m,
        MoexSeriesRegistry.CandlesFutures1h, MoexSeriesRegistry.CandlesFutures1d,
    ];

    public DateOnly? LastParsedPageDate { get; private set; }

    private readonly MoexHistoryPageReader _reader;
    private readonly SeriesRangeDeleter _rangeDeleter;
    private readonly ClickHouseInsertExecutor _executor;
    private readonly int _batchSize;
    private readonly ILogger<SpecLoadHandler> _logger;

    public SpecLoadHandler(
        MoexHistoryPageReader reader,
        SeriesRangeDeleter rangeDeleter,
        ClickHouseInsertExecutor executor,
        ILogger<SpecLoadHandler> logger,
        int batchSize)
    {
        _reader = reader;
        _rangeDeleter = rangeDeleter;
        _executor = executor;
        _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
        _logger = logger;
    }

    public async Task<RowWriteSummary> LoadAsync(MoexLoadTask task, CancellationToken cancellationToken)
    {
        LastParsedPageDate = null;
        MoexSeriesSpec spec = FindSpec(task)
            ?? throw new InvalidOperationException(
                $"Нет обработчика для задачи {task.Id} (data_kind={task.DataKind}, market={task.Market}, interval={task.CandleInterval}).");

        if (spec.DataKind == "candles")
        {
            DateOnly today = MoexTime.Today;
            if (task.DateFrom <= today && task.DateTill >= today)
                throw new InvalidOperationException(
                    $"Историческая загрузка свечей для диапазона {task.DateFrom:yyyy-MM-dd}—{task.DateTill:yyyy-MM-dd} запрещена: " +
                    $"он включает текущую московскую дату {today:yyyy-MM-dd}, которую обслуживает приёмник реального времени.");
        }
        if (spec.DataKind == "tradestats")
            ValidateTaskSecId(task.Secid);

        await _rangeDeleter.DeleteAsync(spec, task.Secid, task.DateFrom, task.DateTill, cancellationToken);

        string operation = spec.Pagination switch
        {
            PaginationKind.DaySplit => MoexOperations.HistoryFutoiFetch,
            PaginationKind.FixedPage => MoexOperations.HistoryCandlesFetch,
            _ => MoexOperations.HistoryCursorFetch,
        };
        MoexOperationTags tags = new(
            MoexLogSources.Algopack, operation,
            MoexDataKinds.FromTaskDataKind(task.DataKind), spec.Market, MoexFlows.History);
        StorageInsertContext insertContext = new(tags.DataKind, tags.Market, MoexFlows.History);

        if (spec.DataKind == "candles")
            return await LoadCandleRangeAsync(spec, task, tags, cancellationToken);
        if (spec.DataKind == "tradestats" && spec.Market == "stock")
            return await LoadTradeStatsRangeAsync<TradeStatsStockRow>(
                spec, task, tags, insertContext, cancellationToken);
        if (spec.DataKind == "tradestats" && spec.Market == "futures")
            return await LoadTradeStatsRangeAsync<TradeStatsFuturesRow>(
                spec, task, tags, insertContext, cancellationToken);
        if (spec.DataKind == "obstats" && spec.Market == "stock")
            return await LoadHistoricalSeriesRangeAsync<ObStatsStockRow>(
                spec, task, tags, insertContext, _reader.ReadObStatsStockPagesAsync, cancellationToken);
        if (spec.DataKind == "obstats" && spec.Market == "futures")
            return await LoadHistoricalSeriesRangeAsync<ObStatsFuturesRow>(
                spec, task, tags, insertContext, _reader.ReadObStatsFuturesPagesAsync, cancellationToken);
        if (spec.DataKind == "orderstats")
            return await LoadHistoricalSeriesRangeAsync<OrderStatsStockRow>(
                spec, task, tags, insertContext, _reader.ReadOrderStatsStockPagesAsync, cancellationToken);
        if (spec.DataKind == "futoi")
            return await LoadHistoricalSeriesRangeAsync<FutoiRow>(
                spec, task, tags, insertContext, _reader.ReadFutoiPagesAsync, cancellationToken);
        if (spec.DataKind == "hi2" && spec.Market == "stock")
            return await LoadHistoricalSeriesRangeAsync<Hi2StockRow>(
                spec, task, tags, insertContext, _reader.ReadHi2StockPagesAsync, cancellationToken);
        if (spec.DataKind == "hi2" && spec.Market == "futures")
            return await LoadHistoricalSeriesRangeAsync<Hi2FuturesRow>(
                spec, task, tags, insertContext, _reader.ReadHi2FuturesPagesAsync, cancellationToken);
        if (spec.DataKind == "mega_alerts" && spec.Market == "stock")
            return await LoadHistoricalSeriesRangeAsync<MegaAlertsStockRow>(
                spec, task, tags, insertContext, _reader.ReadMegaAlertsStockPagesAsync, cancellationToken);
        if (spec.DataKind == "mega_alerts" && spec.Market == "futures")
            return await LoadHistoricalSeriesRangeAsync<MegaAlertsFuturesRow>(
                spec, task, tags, insertContext, _reader.ReadMegaAlertsFuturesPagesAsync, cancellationToken);
        throw new InvalidOperationException($"Нет типизированного обработчика исторического ряда {spec.DataKind}/{spec.Market}.");
    }

    private async Task<RowWriteSummary> LoadCandleRangeAsync(
        MoexSeriesSpec spec, MoexLoadTask task, MoexOperationTags tags, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.Secid))
            throw new InvalidOperationException("Загрузка свечей отвергнута: secid пустой.");

        ProtobufBatchWriter<CandleRow> writer = new(
            _executor, spec.Table, spec.Columns,
            new StorageInsertContext(tags.DataKind, tags.Market, MoexFlows.History));
        HistoricalReadState state = new(_logger, tags, task.Secid);
        long insertedRows = 0;
        bool writeFailed = false;
        try
        {
            DateOnly from = task.DateFrom;
            while (true)
            {
                DateOnly till = GetWindowEnd(spec, task, from);
                async ValueTask AfterPage(CancellationToken ct)
                {
                    UpdateLastParsedPageDate(state, state.PagePhysicalRows > 0);
                    if (writer.RowCount >= _batchSize)
                    {
                        try { insertedRows += await writer.FlushAsync(ct); }
                        catch { writeFailed = true; throw; }
                    }
                }

                try
                {
                    await _reader.ReadCandlePagesAsync(spec, task.Secid, task.Boardid,
                        from, till, tags, writer, state, AfterPage, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (!writeFailed && MoexHistoryPageReader.IsRecoverableReadFailure(ex))
                {
                    UpdateLastParsedPageDate(state, state.PagePhysicalRows > 0);
                    state.RecordReadFailure(from, till, ex.Message);
                    if (writer.RowCount > 0)
                    {
                        try { insertedRows += await writer.FlushAsync(cancellationToken); }
                        catch { writeFailed = true; throw; }
                    }
                }

                if (till == task.DateTill) break;
                from = till.AddDays(1);
            }
            if (writer.RowCount > 0)
            {
                try { insertedRows += await writer.FlushAsync(cancellationToken); }
                catch { writeFailed = true; throw; }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
            await FlushPartialBeforeRethrowAsync(writer, writeFailed, ex, cancellationToken);
            throw;
        }

        ClickHouseWriterLogMessages.RangeWritten(_logger, task.Secid, state.RowsRead, insertedRows);
        return CreateSummary(state);
    }

    private async Task<RowWriteSummary> LoadTradeStatsRangeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec, MoexLoadTask task, MoexOperationTags tags,
        StorageInsertContext insertContext, CancellationToken cancellationToken)
        where TRow : struct
    {
        ProtobufBatchWriter<TRow> statsWriter = new(_executor, spec.Table, spec.Columns, insertContext);
        ProtobufBatchWriter<CandleRow> candleWriter = new(
            _executor, MoexSeriesRegistry.TradeStatsCandleTable, CandleRow.InsertColumns,
            new StorageInsertContext(MoexDataKinds.Candles, tags.Market, MoexFlows.History, 5));
        HistoricalReadState state = new(_logger, tags, task.Secid);
        long insertedRows = 0;
        bool writeFailed = false;
        try
        {
            DateOnly from = task.DateFrom;
            while (true)
            {
                DateOnly till = GetWindowEnd(spec, task, from);
                async ValueTask AfterPage(CancellationToken ct)
                {
                    UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
                    bool flushStats = statsWriter.RowCount >= _batchSize;
                    bool flushCandles = candleWriter.RowCount >= _batchSize;
                    if (flushStats || flushCandles)
                    {
                        try
                        {
                            insertedRows += await FlushTradeStatsWritersAsync(
                                statsWriter, candleWriter, flushStats, flushCandles, ct);
                        }
                        catch { writeFailed = true; throw; }
                    }
                }

                try
                {
                    await _reader.ReadPagesAsync(
                        spec, task.Secid, boardId: null, from, till, tags,
                        statsWriter, candleWriter,
                        lastSourceTime: null, ingestPriority: 1, acceptedCandles: null,
                        state, beforeRequest: null, afterPage: AfterPage, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (!writeFailed && MoexHistoryPageReader.IsRecoverableReadFailure(ex))
                {
                    UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
                    state.RecordReadFailure(from, till, ex.Message);
                    try
                    {
                        insertedRows += await FlushTradeStatsWritersAsync(
                            statsWriter, candleWriter, true, true, cancellationToken);
                    }
                    catch { writeFailed = true; throw; }
                }
                if (till == task.DateTill) break;
                from = till.AddDays(1);
            }

            try
            {
                insertedRows += await FlushTradeStatsWritersAsync(
                    statsWriter, candleWriter, true, true, cancellationToken);
            }
            catch { writeFailed = true; throw; }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
            await FlushPartialTradeStatsBeforeRethrowAsync(
                statsWriter, candleWriter, writeFailed, ex, cancellationToken);
            throw;
        }

        ClickHouseWriterLogMessages.RangeWritten(_logger, task.Secid, state.RowsRead, insertedRows);
        return CreateSummary(state);
    }

    private async Task<RowWriteSummary> LoadHistoricalSeriesRangeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        MoexSeriesSpec spec, MoexLoadTask task, MoexOperationTags tags,
        StorageInsertContext insertContext, MoexHistoryPageReader.ReadSeriesPages<TRow> readPages,
        CancellationToken cancellationToken)
        where TRow : struct
    {
        if (spec.DataKind != "futoi" && string.IsNullOrWhiteSpace(task.Secid))
        {
            string message = spec.DataKind switch
            {
                "obstats" => "Загрузка статистики стакана отвергнута: secid пустой.",
                "orderstats" => "Загрузка статистики заявок отвергнута: secid пустой.",
                "hi2" => "Загрузка концентрации отвергнута: secid пустой.",
                "mega_alerts" => "Загрузка мегаалёртов отвергнута: secid пустой.",
                _ => "Загрузка исторического ряда отвергнута: secid пустой.",
            };
            throw new InvalidOperationException(message);
        }

        ProtobufBatchWriter<TRow> writer = new(_executor, spec.Table, spec.Columns, insertContext);
        HistoricalReadState state = new(_logger, tags, task.Secid);
        long insertedRows = 0;
        bool writeFailed = false;
        try
        {
            DateOnly from = task.DateFrom;
            while (true)
            {
                DateOnly till = GetWindowEnd(spec, task, from);
                async ValueTask AfterPage(CancellationToken ct)
                {
                    UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
                    if (writer.RowCount >= _batchSize)
                    {
                        try { insertedRows += await writer.FlushAsync(ct); }
                        catch { writeFailed = true; throw; }
                    }
                }

                try
                {
                    await readPages(spec, task.Secid, from, till, tags, writer, null,
                        state, null, AfterPage, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (!writeFailed && MoexHistoryPageReader.IsRecoverableReadFailure(ex))
                {
                    UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
                    state.RecordReadFailure(from, till, ex.Message);
                    if (writer.RowCount > 0)
                    {
                        try { insertedRows += await writer.FlushAsync(cancellationToken); }
                        catch { writeFailed = true; throw; }
                    }
                }
                if (till == task.DateTill) break;
                from = till.AddDays(1);
            }

            if (writer.RowCount > 0)
            {
                try { insertedRows += await writer.FlushAsync(cancellationToken); }
                catch { writeFailed = true; throw; }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UpdateLastParsedPageDate(state, state.PageAcceptedRows + state.PageSkippedRows > 0);
            await FlushPartialBeforeRethrowAsync(writer, writeFailed, ex, cancellationToken);
            throw;
        }

        ClickHouseWriterLogMessages.RangeWritten(_logger, task.Secid, state.RowsRead, insertedRows);
        return CreateSummary(state);
    }

    private static async Task<long> FlushTradeStatsWritersAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> statsWriter, ProtobufBatchWriter<CandleRow> candleWriter,
        bool flushStats, bool flushCandles, CancellationToken cancellationToken)
        where TRow : struct
    {
        long statsInserted = 0;
        List<Exception>? failures = null;
        if (flushStats && statsWriter.RowCount > 0)
        {
            try { statsInserted = await statsWriter.FlushAsync(cancellationToken); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (flushCandles && candleWriter.RowCount > 0)
        {
            try { _ = await candleWriter.FlushAsync(cancellationToken); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (cancellationToken.IsCancellationRequested && failures is { Count: > 0 }
            && failures[0] is OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is { Count: > 1 }) throw new AggregateException("Обе вставки TradeStats завершились с ошибкой.", failures);
        return statsInserted;
    }

    private async Task FlushPartialBeforeRethrowAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> writer, bool writeFailed, Exception readFailure, CancellationToken cancellationToken)
        where TRow : struct
    {
        if (writer.RowCount == 0 || writeFailed || cancellationToken.IsCancellationRequested)
            return;
        try { _ = await writer.FlushAsync(cancellationToken); }
        catch (Exception flushFailure)
        {
            throw new AggregateException("Ошибка чтения и последующая ошибка отправки уже разобранных строк.", readFailure, flushFailure);
        }
    }

    private async Task FlushPartialTradeStatsBeforeRethrowAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>(
        ProtobufBatchWriter<TRow> statsWriter, ProtobufBatchWriter<CandleRow> candleWriter,
        bool writeFailed, Exception readFailure, CancellationToken cancellationToken)
        where TRow : struct
    {
        if (writeFailed || cancellationToken.IsCancellationRequested
            || (statsWriter.RowCount == 0 && candleWriter.RowCount == 0))
            return;
        try { _ = await FlushTradeStatsWritersAsync(statsWriter, candleWriter, true, true, cancellationToken); }
        catch (Exception flushFailure)
        {
            throw new AggregateException("Ошибка чтения и последующая ошибка отправки уже разобранных строк.", readFailure, flushFailure);
        }
    }

    private static RowWriteSummary CreateSummary(HistoricalReadState state)
        => new(state.RowsRead, state.RowsSkipped, state.ReadErrorCount,
            state.FirstReadErrorReason, state.FirstReadErrorDateFrom,
            state.FirstReadErrorDateTill, state.FirstSkippedDate);

    private void UpdateLastParsedPageDate(HistoricalReadState state, bool hasRows)
    {
        if (!hasRows)
            return;
        LastParsedPageDate = state.PageLastSourceTime is DateTime time && time != default
            ? DateOnly.FromDateTime(time)
            : null;
    }

    private static DateOnly GetWindowEnd(MoexSeriesSpec spec, MoexLoadTask task, DateOnly from)
    {
        int days = spec.DataKind == "orderstats" && spec.Market == "stock" ? 7 : 364;
        int remainingDays = task.DateTill.DayNumber - from.DayNumber;
        return remainingDays < days ? task.DateTill : from.AddDays(days - 1);
    }

    private static MoexSeriesSpec? FindSpec(MoexLoadTask task)
    {
        for (int i = 0; i < MigratedSpecs.Length; i++)
        {
            MoexSeriesSpec spec = MigratedSpecs[i];
            if (task.DataKind == spec.DataKind && task.Market == spec.Market && task.CandleInterval == spec.CandleInterval)
                return spec;
        }
        return null;
    }

    private static void ValidateTaskSecId(string secid)
    {
        if (!string.IsNullOrWhiteSpace(secid)) return;
        throw new InvalidOperationException("Загрузка статистики сделок отвергнута: secid пустой.");
    }
}
