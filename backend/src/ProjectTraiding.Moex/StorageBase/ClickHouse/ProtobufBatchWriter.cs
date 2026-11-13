using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>
/// Собирает типизированные строки в одну length-delimited Protobuf-пачку для ClickHouse.
/// Экземпляр принадлежит одной операции; вызовы Append и FlushAsync не выполняются параллельно.
/// </summary>
// Аннотация нужна по trimming-контракту TypeModel.Measure<T>; сериализует сгенерированная модель.
public sealed class ProtobufBatchWriter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRow>
    where TRow : struct
{
    private readonly ClickHouseInsertExecutor _executor;
    private readonly string _table;
    private readonly IReadOnlyList<string> _columns;
    private readonly StorageInsertContext _insertContext;
    private readonly ArrayBufferWriter<byte> _buffer = new ArrayBufferWriter<byte>();
    private int _rowCount;
    private bool _isFaulted;

    public ProtobufBatchWriter(
        ClickHouseInsertExecutor executor,
        string table,
        IReadOnlyList<string> columns,
        StorageInsertContext insertContext)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0)
            throw new ArgumentException("At least one target column is required.", nameof(columns));

        _executor = executor;
        _table = table;
        _columns = columns;
        _insertContext = insertContext;
    }

    public int RowCount => _rowCount;

    public int WrittenBytes => _buffer.WrittenCount;

    public void Append(in TRow row)
    {
        if (_isFaulted)
            throw new InvalidOperationException("This Protobuf batch is unusable after a failed append or flush.");

        int nextRowCount = checked(_rowCount + 1);

        try
        {
            using (MeasureState<TRow> measured = MarketProtobufModel.Instance.Measure(row))
            {
                WriteVarint(checked((ulong)measured.Length));
                measured.Serialize(_buffer);
            }

            _rowCount = nextRowCount;
        }
        catch
        {
            // При ошибке Serialize могла записать часть сообщения; такую пачку нельзя отправлять.
            _isFaulted = true;
            throw;
        }
    }

    public async Task<long> FlushAsync(CancellationToken cancellationToken)
    {
        if (_isFaulted)
            throw new InvalidOperationException("This Protobuf batch is unusable after a failed append or flush.");

        if (_rowCount == 0)
            return 0;

        long insertedRows;
        try
        {
            insertedRows = await _executor.InsertProtobufAsync(
                _table,
                _columns,
                _buffer.WrittenMemory,
                _rowCount,
                _insertContext,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _isFaulted = true;
            throw;
        }

        _buffer.ResetWrittenCount();
        _rowCount = 0;
        return insertedRows;
    }

    private void WriteVarint(ulong value)
    {
        Span<byte> span = _buffer.GetSpan(10);
        int length = 0;

        while (value >= 0x80)
        {
            span[length++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        span[length++] = (byte)value;
        _buffer.Advance(length);
    }
}
