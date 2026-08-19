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
        string tokenPrefix,
        string emptySecidMessage,
        int keyTimeIndex,
        int tradeNoIndex)
    {
        RootKey = rootKey;
        SourceColumns = sourceColumns;
        Table = table;
        TargetColumns = targetColumns;
        TokenPrefix = tokenPrefix;
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
        for (int i = 0; i < targetColumns.Length; i++)
        {
            columns[i] = targetColumns[i].Name;
            columnTypes[columns[i]] = targetColumns[i].ColumnType;

            if (targetColumns[i].SourceIndex >= 0)
                sourceUsed[targetColumns[i].SourceIndex] = true;
            if (targetColumns[i].SecondSourceIndex >= 0)
                sourceUsed[targetColumns[i].SecondSourceIndex] = true;
        }

        Columns = columns;
        ColumnTypes = columnTypes;
        SourceColumnUsed = sourceUsed;
    }

    /// <summary>Имя корневого блока ответа: "trades", "orderbook" или "candles".</summary>
    public string RootKey { get; }

    public SourceColumn[] SourceColumns { get; }

    public string Table { get; }

    public TargetColumn[] TargetColumns { get; }

    public string TokenPrefix { get; }

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
}
