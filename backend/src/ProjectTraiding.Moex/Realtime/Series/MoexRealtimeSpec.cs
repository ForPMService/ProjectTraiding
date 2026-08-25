using System.Text;
using ProjectTraiding.Moex.Series;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Декларация одного вида данных приёма: что запрашивать у биржи, что проверять в ответе
/// и какую строку вставки собирать. Паспорт исторической загрузки построен вокруг понятий
/// загрузки — шаблон адреса, вид пагинации, свечной интервал; у приёма этих понятий нет,
/// поэтому тип отдельный. Дублирование дешевле неправильной абстракции.
///
/// Все производные значения вычисляются один раз при создании: в горячем цикле опроса
/// не должно быть ни склейки имён колонок, ни поиска по словарю.
/// </summary>
public sealed class MoexRealtimeSpec
{
    public MoexRealtimeSpec(
        string rootKey,
        SourceColumn[] sourceColumns,
        string table,
        TargetColumn[] targetColumns,
        string emptySecidMessage,
        int keyTimeIndex,
        int tradeNoIndex)
    {
        RootKey = rootKey;
        SourceColumns = sourceColumns;
        Table = table;
        TargetColumns = targetColumns;
        EmptySecidMessage = emptySecidMessage;
        KeyTimeIndex = keyTimeIndex;
        TradeNoIndex = tradeNoIndex;

        string[] sourceNames = new string[sourceColumns.Length];
        for (int i = 0; i < sourceColumns.Length; i++)
            sourceNames[i] = Encoding.UTF8.GetString(sourceColumns[i].Name);
        ColumnsParam = string.Join(',', sourceNames);

        string[] columns = new string[targetColumns.Length];
        Dictionary<string, string> columnTypes = new(targetColumns.Length);
        bool[] sourceUsed = new bool[sourceColumns.Length];
        int[] momentDateFirstUses = new int[sourceColumns.Length];
        int[] momentTimeSecondUses = new int[sourceColumns.Length];
        int sessionDateIndex = -1;
        for (int i = 0; i < targetColumns.Length; i++)
        {
            TargetColumn target = targetColumns[i];
            columns[i] = target.Name;
            columnTypes[columns[i]] = target.ColumnType;

            if (target.SourceIndex >= 0)
            {
                sourceUsed[target.SourceIndex] = true;
                if (sourceColumns[target.SourceIndex].Kind == ColumnKind.MomentDate)
                    momentDateFirstUses[target.SourceIndex]++;
            }
            if (target.SecondSourceIndex >= 0)
            {
                sourceUsed[target.SecondSourceIndex] = true;
                if (sourceColumns[target.SecondSourceIndex].Kind == ColumnKind.MomentTime)
                    momentTimeSecondUses[target.SecondSourceIndex]++;
            }

            if (target.FillRule == FillRule.SourceDateTime)
            {
                EnsureSourceColumnKind(target, target.SourceIndex, ColumnKind.MomentDate);
                EnsureSourceColumnKind(target, target.SecondSourceIndex, ColumnKind.MomentTime);
            }
            else
            {
                EnsureMomentSourceIsNotUsed(target, target.SourceIndex);
                EnsureMomentSourceIsNotUsed(target, target.SecondSourceIndex);
            }

            if (target.FillRule == FillRule.SessionDate)
                sessionDateIndex = i;
        }

        for (int i = 0; i < sourceColumns.Length; i++)
        {
            if (sourceColumns[i].Kind == ColumnKind.MomentDate && momentDateFirstUses[i] != 1)
                ThrowInvalidMomentSourceUse(i, sourceColumns[i].Kind, momentDateFirstUses[i]);

            if (sourceColumns[i].Kind == ColumnKind.MomentTime && momentTimeSecondUses[i] != 1)
                ThrowInvalidMomentSourceUse(i, sourceColumns[i].Kind, momentTimeSecondUses[i]);
        }

        Columns = columns;
        ColumnTypes = columnTypes;
        SourceColumnUsed = sourceUsed;
        SessionDateIndex = sessionDateIndex;
    }

    private void EnsureSourceColumnKind(TargetColumn target, int sourceIndex, ColumnKind expected)
    {
        ColumnKind? actual = sourceIndex >= 0 && sourceIndex < SourceColumns.Length
            ? SourceColumns[sourceIndex].Kind
            : null;
        if (actual != expected)
        {
            string actualText = actual?.ToString() ?? "отсутствует";
            throw new InvalidOperationException(
                $"Ошибка декларации [{RootKey}] таблица {Table}: колонка {target.Name} " +
                $"ожидала источник вида {expected} на позиции {sourceIndex}, " +
                $"получен {actualText}.");
        }
    }

    private void EnsureMomentSourceIsNotUsed(TargetColumn target, int sourceIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= SourceColumns.Length)
            return;

        ColumnKind kind = SourceColumns[sourceIndex].Kind;
        if (kind is ColumnKind.MomentDate or ColumnKind.MomentTime)
        {
            throw new InvalidOperationException(
                $"Ошибка декларации [{RootKey}] таблица {Table}: колонка {target.Name} " +
                $"ссылается на источник вида {kind} на позиции {sourceIndex}, " +
                "хотя такой источник разрешён только для момента строки.");
        }
    }

    private void ThrowInvalidMomentSourceUse(int sourceIndex, ColumnKind kind, int uses)
    {
        throw new InvalidOperationException(
            $"Ошибка декларации [{RootKey}] таблица {Table}: источник позиции {sourceIndex} " +
            $"вида {kind} использован {uses} раз вместо одного разрешённого использования.");
    }

    /// <summary>Имя корневого блока ответа: "trades", "orderbook" или "candles".</summary>
    public string RootKey { get; }

    public SourceColumn[] SourceColumns { get; }

    public string Table { get; }

    public TargetColumn[] TargetColumns { get; }


    /// <summary>Сообщение отказа при пустом коде инструмента. Своё у каждого вида данных.</summary>
    public string EmptySecidMessage { get; }

    /// <summary>
    /// Позиция ключевого времени в целевой строке. У сделок и стакана это момент строки,
    /// задающий границы пачки; у свечей — начало свечи, по которому служба отбирает закрытые.
    /// Индекс, а не имя: поиск колонки по имени в цикле запрещён.
    /// </summary>
    public int KeyTimeIndex { get; }

    /// <summary>
    /// Позиция номера сделки в целевой строке или -1, если у вида данных номера нет.
    /// Нужна, чтобы отдать службе номер типизированным значением, а не обращением
    /// к строке по позиции с приведением типа.
    /// </summary>
    public int TradeNoIndex { get; }

    /// <summary>Готовое значение параметра запроса с перечнем колонок.</summary>
    public string ColumnsParam { get; }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyDictionary<string, string> ColumnTypes { get; }

    /// <summary>
    /// Признак востребованности колонки источника по её позиции: истина, если на позицию
    /// ссылается хоть одна целевая колонка. Значение невостребованной колонки не попадает
    /// никуда, поэтому разборщик сверяет вид её токена, но строку из неё не создаёт.
    /// </summary>
    public bool[] SourceColumnUsed { get; }

    /// <summary>
    /// Положение колонки даты сессии в целевой строке или −1, если такой колонки нет.
    /// Дата сессии приходит отдельным блоком ответа, который источник отдаёт после таблицы,
    /// поэтому её значение проставляется в готовые строки после разбора всего тела.
    /// </summary>
    public int SessionDateIndex { get; }
}
