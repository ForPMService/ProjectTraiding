using ProjectTraiding.Moex.StorageBase.ClickHouse;
using System.Diagnostics.CodeAnalysis;

namespace ProjectTraiding.Moex.Series;

/// <summary>
/// Holds only the unresolved suffix of a HI2 or MegaAlerts page. Rows before the
/// current time-group boundary are appended as soon as that boundary is known.
/// </summary>
internal sealed class HistoricalSeriesTimeGroupTail<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow> where TRow : struct
{
    private readonly ProtobufBatchWriter<TRow> _writer;
    private readonly DateTime? _cursorSourceTime;
    private readonly List<Entry> _pending = [];
    private readonly List<SkippedSourceRow> _skipped = [];
    private int _sourceRowsCount;
    private int _acceptedRowsCount;
    private int _parsedValidRowsCount;
    private DateTime? _lastSourceTime;
    private DateTime? _maxAcceptedSourceTime;
    private int _pendingParsedValidRowsCount;

    public HistoricalSeriesTimeGroupTail(
        ProtobufBatchWriter<TRow> writer,
        DateTime? lastSourceTime = null)
    {
        _writer = writer;
        _cursorSourceTime = lastSourceTime;
    }

    public int StableSourceRowsCount => _sourceRowsCount;
    public int PendingSourceRowsCount => _pending.Count;
    public int PendingParsedValidRowsCount => _pendingParsedValidRowsCount;
    public int FullSourceRowsCount => checked(_sourceRowsCount + _pending.Count);

    public HistoricalSeriesParsedPage PrefixPage => new(
        _sourceRowsCount,
        _acceptedRowsCount,
        _skipped.Count == 0 ? null : _skipped,
        _lastSourceTime)
    {
        MaxAcceptedSourceTime = _maxAcceptedSourceTime,
        ParsedValidRowsCount = _parsedValidRowsCount,
    };

    public void AddAccepted(int sourceIndex, DateTime? time, in TRow row)
    {
        _pending.Add(new Entry(sourceIndex, time, row, null, Accepted: true));
        _pendingParsedValidRowsCount++;
        CommitStablePrefix();
    }

    public void AddSkipped(int sourceIndex, DateTime? time, string reason)
    {
        _pending.Add(new Entry(sourceIndex, time, default, reason, Accepted: false));
        CommitStablePrefix();
    }

    public HistoricalSeriesParsedPage CompletePage()
    {
        CommitPrefix(_pending.Count);
        return PrefixPage;
    }

    private void CommitStablePrefix()
    {
        int tailStart = FindTailGroupStart();
        if (tailStart > 0)
            CommitPrefix(tailStart);
    }

    // This mirrors MoexHistoryPageReader.FindTailGroupStart: unknown timestamps
    // stay attached to the nearest known group on their right when one exists.
    private int FindTailGroupStart()
    {
        if (_pending.Count == 0)
            return 0;

        DateTime? groupTime = null;
        for (int index = _pending.Count - 1; index >= 0; index--)
        {
            DateTime? time = _pending[index].Time;
            if (groupTime is null)
            {
                groupTime = time;
            }
            else if (time is not null && time != groupTime)
            {
                return index + 1;
            }
        }

        return 0;
    }

    private void CommitPrefix(int count)
    {
        for (int index = 0; index < count; index++)
        {
            Entry entry = _pending[index];
            if (entry.Accepted)
            {
                _parsedValidRowsCount++;
                _pendingParsedValidRowsCount--;
                if (IsAtOrAfterCursor(entry.Time))
                {
                    TRow row = entry.Row;
                    _writer.Append(in row);
                    _acceptedRowsCount++;
                    if (entry.Time is DateTime acceptedTime
                        && (_maxAcceptedSourceTime is null || acceptedTime > _maxAcceptedSourceTime.Value))
                    {
                        _maxAcceptedSourceTime = acceptedTime;
                    }
                }
            }
            else
            {
                _skipped.Add(new SkippedSourceRow(
                    entry.SourceIndex, entry.Time, entry.Reason!));
            }

            _sourceRowsCount++;
            _lastSourceTime = entry.Time;
        }

        if (count > 0)
            _pending.RemoveRange(0, count);
    }

    private bool IsAtOrAfterCursor(DateTime? sourceTime)
        => _cursorSourceTime is null
            || sourceTime is DateTime value && value >= _cursorSourceTime.Value;

    private readonly record struct Entry(
        int SourceIndex,
        DateTime? Time,
        TRow Row,
        string? Reason,
        bool Accepted);
}
