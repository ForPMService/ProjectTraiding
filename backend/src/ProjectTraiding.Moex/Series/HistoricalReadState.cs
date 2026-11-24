using System.Diagnostics;
using System.Globalization;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Series;

/// <summary>
/// Scalar accounting and request context owned by one historical read operation.
/// It deliberately contains no source rows or page summaries.
/// </summary>
internal sealed class HistoricalReadState
{
    private readonly ILogger _logger;
    private readonly MoexOperationTags _operationTags;
    private readonly string _secId;
    private readonly DateTime? _confirmedSourceTime;

    private string _method = string.Empty;
    private string? _from;
    private string? _till;
    private string _pagePosition = "initial";
    private int _unreportedSkippedRows;

    public HistoricalReadState(
        ILogger logger,
        MoexOperationTags operationTags,
        string secId,
        DateTime? confirmedSourceTime = null)
    {
        _logger = logger;
        _operationTags = operationTags;
        _secId = secId;
        _confirmedSourceTime = confirmedSourceTime;
    }

    public long RowsRead { get; private set; }
    public long RowsSkipped { get; private set; }
    public long RowsAccepted { get; private set; }
    public long ParsedValidRows { get; private set; }
    public long PhysicalRows { get; private set; }
    public long RowsReceivedMetric { get; private set; }
    public int SignificantSkippedRows { get; private set; }
    public DateTime? LastSourceTime { get; private set; }
    public DateTime? MaxAcceptedSourceTime { get; private set; }
    public DateOnly? FirstSkippedDate { get; private set; }
    public int ReadErrorCount { get; private set; }
    public string? FirstReadErrorReason { get; private set; }
    public DateOnly? FirstReadErrorDateFrom { get; private set; }
    public DateOnly? FirstReadErrorDateTill { get; private set; }

    public int PagePhysicalRows { get; private set; }
    public int PageAcceptedRows { get; private set; }
    public int PageParsedValidRows { get; private set; }
    public int PageRowsReceivedMetric { get; private set; }
    public int PageSkippedRows { get; private set; }
    public DateTime? PageLastSourceTime { get; private set; }
    public TimeSpan LastPageElapsed { get; set; }
    public string PageMethod => _method;
    public string? PageFrom => _from;
    public string? PageTill => _till;
    public string PagePosition => _pagePosition;

    public void BeginPage(
        string method,
        DateOnly from,
        DateOnly till,
        string pagePosition)
    {
        _method = method;
        _from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _till = till.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _pagePosition = pagePosition;
        PagePhysicalRows = 0;
        PageAcceptedRows = 0;
        PageParsedValidRows = 0;
        PageRowsReceivedMetric = 0;
        PageSkippedRows = 0;
        PageLastSourceTime = null;
        LastPageElapsed = TimeSpan.Zero;
    }

    public void RecordPhysicalRow(bool covered = false, bool receivedMetric = false)
    {
        PagePhysicalRows++;
        PhysicalRows++;
        if (covered)
            RowsRead++;
        if (receivedMetric)
            RecordRowsReceivedMetric();
    }

    public void RecordParsedValidRow(bool receivedMetric = false)
    {
        PageParsedValidRows++;
        ParsedValidRows++;
        if (receivedMetric)
            RecordRowsReceivedMetric();
    }

    public void RecordAccepted(DateTime? sourceTime, bool covered = true, bool receivedMetric = false)
    {
        PageAcceptedRows++;
        RowsAccepted++;
        if (covered)
            RowsRead++;
        if (sourceTime is DateTime time)
        {
            PageLastSourceTime = time;
            LastSourceTime = time;
            if (MaxAcceptedSourceTime is null || time > MaxAcceptedSourceTime.Value)
                MaxAcceptedSourceTime = time;
        }
        if (receivedMetric)
            RecordRowsReceivedMetric();
    }

    public void RecordSkipped(
    int sourceIndex, DateTime? sourceTime, string reason,
    int? columnIndex = null, string? columnName = null)
    => RecordSkipped(sourceIndex, sourceTime, reason,
        _method, _from, _till, _pagePosition, columnIndex, columnName);

    public void RecordSkipped(
        int sourceIndex,
        DateTime? sourceTime,
        string reason,
        string method,
        string? from,
        string? till,
        string pagePosition,
        int? columnIndex = null,
        string? columnName = null)
    {
        PageSkippedRows++;
        RowsSkipped++;
        bool isBeforeConfirmedCursor = _confirmedSourceTime is DateTime cursor
            && sourceTime is DateTime time
            && time < cursor;
        if (!isBeforeConfirmedCursor)
        {
            RowsRead++;
            SignificantSkippedRows++;
        }
        _unreportedSkippedRows++;
        if (sourceTime is DateTime skippedTime && skippedTime != default)
            FirstSkippedDate ??= DateOnly.FromDateTime(skippedTime);

        MoexLogMessages.HistoryRowSkipped(
            _logger,
            _secId,
            _operationTags.DataKind,
            _operationTags.Operation,
            method,
            from,
            till,
            pagePosition,
            sourceIndex,
            sourceTime,
            columnIndex,
            columnName,
            reason);
    }

    public void RecordReadFailure(DateOnly from, DateOnly till, string reason)
    {
        ReadErrorCount++;
        if (FirstReadErrorReason is not null)
            return;
        FirstReadErrorReason = reason;
        FirstReadErrorDateFrom = from;
        FirstReadErrorDateTill = till;
    }

    public void SetPageLastSourceTime(DateTime? sourceTime)
    {
        PageLastSourceTime = sourceTime;
        if (sourceTime is DateTime value)
            LastSourceTime = value;
    }

    public void RecordRowsReceivedMetric(int count = 1)
    {
        if (count <= 0)
            return;
        PageRowsReceivedMetric = checked(PageRowsReceivedMetric + count);
        RowsReceivedMetric = checked(RowsReceivedMetric + count);
    }

    public void RecordSkippedDiagnostics()
    {
        if (_unreportedSkippedRows == 0)
            return;

        int skippedRows = _unreportedSkippedRows;
        _unreportedSkippedRows = 0;
        MoexLogMessages.RowsSkipped(_logger, _method, skippedRows);
        MoexMetrics.RowsSkipped.Add(
            skippedRows,
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Source, _operationTags.Source),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.DataKind, _operationTags.DataKind),
            new KeyValuePair<string, object?>(MoexTelemetryAttributes.Market, _operationTags.Market));
    }

    public void RecordReceivedMetrics()
    {
        TagList rowsTags = new()
        {
            { MoexTelemetryAttributes.Source, _operationTags.Source },
            { MoexTelemetryAttributes.DataKind, _operationTags.DataKind },
            { MoexTelemetryAttributes.Market, _operationTags.Market },
            { MoexTelemetryAttributes.Flow, _operationTags.Flow },
        };
        MoexMetrics.RowsReceived.Add(PageRowsReceivedMetric, rowsTags);
    }
}
