using System.Diagnostics.CodeAnalysis;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>Holds only the unresolved final timestamp group for one cursor page.</summary>
internal sealed class HistoricalSeriesTimeGroupTail<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow> where TRow : struct
{
    private readonly ProtobufBatchWriter<TRow> _writer;
    private readonly HistoricalReadState _state;
    private readonly DateTime? _confirmedSourceTime;
    private readonly string _method;
    private readonly string? _from;
    private readonly string? _till;
    private readonly string _pagePosition;
    private readonly List<Entry> _pending = [];
    private int _stableSourceRowsCount;
    private int _pendingParsedValidRowsCount;

    public HistoricalSeriesTimeGroupTail(
        ProtobufBatchWriter<TRow> writer,
        HistoricalReadState state,
        DateTime? confirmedSourceTime = null)
    {
        _writer = writer;
        _state = state;
        _confirmedSourceTime = confirmedSourceTime;
        _method = state.PageMethod;
        _from = state.PageFrom;
        _till = state.PageTill;
        _pagePosition = state.PagePosition;
    }

    public int StableSourceRowsCount => _stableSourceRowsCount;
    public int PendingSourceRowsCount => _pending.Count;
    public int PendingParsedValidRowsCount => _pendingParsedValidRowsCount;
    public int FullSourceRowsCount => checked(_stableSourceRowsCount + _pending.Count);

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

    public void CommitComplete()
    {
        CommitPrefix(_pending.Count, receivedMetric: false);
    }

    /// <summary>Accounts pending refusals only when the operation cannot continue the group.</summary>
    public void RecordPendingSkipsFrom(int sourceOffset = 0)
    {
        for (int index = Math.Max(0, sourceOffset); index < _pending.Count; index++)
        {
            Entry entry = _pending[index];
            if (!entry.Accepted)
                _state.RecordSkipped(
                    entry.SourceIndex, entry.Time, entry.Reason!, _method, _from, _till, _pagePosition);
        }
    }

    private void CommitStablePrefix()
    {
        int tailStart = FindTailGroupStart();
        if (tailStart > 0)
            CommitPrefix(tailStart);
    }

    private int FindTailGroupStart()
    {
        if (_pending.Count == 0)
            return 0;

        DateTime? groupTime = null;
        for (int index = _pending.Count - 1; index >= 0; index--)
        {
            DateTime? time = _pending[index].Time;
            if (groupTime is null)
                groupTime = time;
            else if (time is not null && time != groupTime)
                return index + 1;
        }
        return 0;
    }

    private void CommitPrefix(int count, bool receivedMetric = true)
    {
        for (int index = 0; index < count; index++)
        {
            Entry entry = _pending[index];
            _state.SetPageLastSourceTime(entry.Time);
            if (entry.Accepted)
            {
                _pendingParsedValidRowsCount--;
                _state.RecordParsedValidRow(receivedMetric);
                bool beforeCursor = _confirmedSourceTime is DateTime cursor
                    && entry.Time is DateTime time
                    && time < cursor;
                if (!beforeCursor)
                {
                    TRow row = entry.Row;
                    _writer.Append(in row);
                    _state.RecordAccepted(entry.Time);
                }
            }
            else
            {
                _state.RecordSkipped(
                    entry.SourceIndex, entry.Time, entry.Reason!, _method, _from, _till, _pagePosition);
            }
            _stableSourceRowsCount++;
        }

        if (count > 0)
            _pending.RemoveRange(0, count);
    }

    private readonly record struct Entry(
        int SourceIndex,
        DateTime? Time,
        TRow Row,
        string? Reason,
        bool Accepted);
}
