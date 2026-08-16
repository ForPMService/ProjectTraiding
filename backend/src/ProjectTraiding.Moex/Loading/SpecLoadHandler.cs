using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Loading;

public sealed class SpecLoadHandler
{
    private static readonly MoexSeriesSpec[] MigratedSpecs =
    [
        MoexSeriesRegistry.Hi2Stock,
        MoexSeriesRegistry.Hi2Futures,
        MoexSeriesRegistry.TradeStatsStock,
        MoexSeriesRegistry.TradeStatsFutures,
        MoexSeriesRegistry.ObStatsStock,
        MoexSeriesRegistry.ObStatsFutures,
        MoexSeriesRegistry.OrderStatsStock,
        MoexSeriesRegistry.MegaAlertsStock,
        MoexSeriesRegistry.MegaAlertsFutures,
        MoexSeriesRegistry.Futoi,
        MoexSeriesRegistry.CandlesStock1m,
        MoexSeriesRegistry.CandlesStock10m,
        MoexSeriesRegistry.CandlesStock1h,
        MoexSeriesRegistry.CandlesStock1d,
        MoexSeriesRegistry.CandlesFutures1m,
        MoexSeriesRegistry.CandlesFutures10m,
        MoexSeriesRegistry.CandlesFutures1h,
        MoexSeriesRegistry.CandlesFutures1d,
    ];

    private readonly MoexHistoryPageReader _reader;
    private readonly MoexHistoryWriter _writer;

    public SpecLoadHandler(MoexHistoryPageReader reader, MoexHistoryWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public async Task<RowWriteSummary> LoadAsync(
        MoexLoadTask task,
        LoadStopOutcome stopOutcome,
        CancellationToken cancellationToken)
    {
        MoexSeriesSpec spec = FindSpec(task)
            ?? throw new InvalidOperationException(
                $"Нет обработчика для задачи {task.Id} (data_kind={task.DataKind}, market={task.Market}, interval={task.CandleInterval}).");

        string operation = spec.Pagination switch
        {
            PaginationKind.DaySplit => MoexOperations.HistoryFutoiFetch,
            PaginationKind.FixedPage => MoexOperations.HistoryCandlesFetch,
            _ => MoexOperations.HistoryCursorFetch,
        };
        MoexOperationTags operationTags = new(
            MoexLogSources.Algopack,
            operation,
            MoexDataKinds.FromTaskDataKind(task.DataKind),
            spec.Market,
            MoexFlows.History);

        IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> pages = _reader.ReadPages(
            spec,
            task.Secid,
            task.Boardid,
            task.DateFrom,
            task.DateTill,
            stopOutcome,
            operationTags,
            cancellationToken);

        StorageInsertContext insertContext = new(
            operationTags.DataKind,
            operationTags.Market,
            MoexFlows.History);

        return await _writer.WriteRangeAsync(
            spec,
            task.Secid,
            task.SourceContractVersion,
            task.WriterVersion,
            pages,
            insertContext,
            cancellationToken);
    }

    private static MoexSeriesSpec? FindSpec(MoexLoadTask task)
    {
        for (int i = 0; i < MigratedSpecs.Length; i++)
        {
            MoexSeriesSpec spec = MigratedSpecs[i];
            if (task.DataKind == spec.DataKind
                && task.Market == spec.Market
                && task.CandleInterval == spec.CandleInterval)
                return spec;
        }

        return null;
    }
}
