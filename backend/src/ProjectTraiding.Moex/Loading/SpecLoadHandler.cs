using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Loading;

public sealed class SpecLoadHandler : ILoadHandler
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
    ];

    private readonly MoexHistoryPageReader _reader;
    private readonly MoexHistoryWriter _writer;

    public SpecLoadHandler(MoexHistoryPageReader reader, MoexHistoryWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public bool CanHandle(MoexLoadTask task) => FindSpec(task) is not null;

    public async Task<RowWriteSummary> LoadAsync(
        MoexLoadTask task,
        LoadStopOutcome stopOutcome,
        CancellationToken cancellationToken)
    {
        MoexSeriesSpec spec = FindSpec(task)
            ?? throw new InvalidOperationException(
                $"Для {task.DataKind}/{task.Market} нет переведённой декларации.");

        string operation = spec.Pagination == PaginationKind.DaySplit
            ? MoexOperations.HistoryFutoiFetch
            : MoexOperations.HistoryCursorFetch;
        MoexOperationTags operationTags = new(
            MoexLogSources.Algopack,
            operation,
            spec.TelemetryDataKind,
            spec.Market,
            MoexFlows.History);

        IAsyncEnumerable<List<(object?[] Row, DateTime Time)>> pages = _reader.ReadPages(
            spec,
            task.Secid,
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
            if (task.DataKind == spec.DataKind && task.Market == spec.Market)
                return spec;
        }

        return null;
    }
}
