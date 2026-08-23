using System.Text;

namespace ProjectTraiding.Moex.Series;

public enum ColumnKind
{
    String,
    Int32,
    Int64,
    Double,
    DateTime,

    /// <summary>
    /// Дата момента строки, образец "yyyy-MM-dd". Читается из байтов ответа и в общий
    /// массив значений строки не попадает: её потребитель один — правило момента.
    /// </summary>
    MomentDate,

    /// <summary>
    /// Время суток момента строки, образец "HH:mm:ss". Читается из байтов ответа и в общий
    /// массив значений строки не попадает: её потребитель один — правило момента.
    /// </summary>
    MomentTime,
}

/// <summary>
/// Накопитель частей момента на время разбора одной строки данных. Живёт в кадре стека
/// разборщика и передаётся по ссылке, поэтому объектов в куче не создаёт.
///
/// Три состояния каждой части различаются без отдельного перечисления: заполнено значение —
/// часть годна; заполнен исходный текст — часть не уложилась в строгий образец;
/// не заполнено ничего — часть отсутствует.
///
/// Исходный текст сохраняется только на пути отказа. На счастливом пути оба поля текста
/// остаются пустыми, и строк не создаётся.
/// </summary>
public struct SourceTimeParts
{
    public DateOnly? Date;
    public TimeOnly? Time;
    public string? RawDate;
    public string? RawTime;

    public void Reset()
    {
        Date = null;
        Time = null;
        RawDate = null;
        RawTime = null;
    }
}

public enum FillRule
{
    TaskSecId,
    Direct,
    SourceDateTime,
    WallClock,
    ExternalSecId,
    Constant,

    /// <summary>
    /// Дата торговой сессии из блока версии данных ответа. Значение общее для всей страницы
    /// и разбирается один раз на ответ, поэтому позиции в строке источника у правила нет.
    /// Пустое значение оставляет столбец пустым. Только в декларациях приёма.
    /// </summary>
    SessionDate,

    /// <summary>
    /// Момент снимка, выведенный из порядкового номера. В ответе стакана даты нет,
    /// поле обновления даёт только время суток, поэтому момент выводится из числа
    /// в форме ГГГГММДДЧЧммсс. Только в декларациях приёма.
    /// </summary>
    SnapshotTimeFromSeqNum,
}

public enum PaginationKind
{
    Cursor,
    DaySplit,
    FixedPage,
}

public readonly record struct SourceColumn(
    int Position,
    byte[] Name,
    ColumnKind Kind);

public readonly record struct TargetColumn(
    string Name,
    string ColumnType,
    FillRule FillRule,
    int SourceIndex = -1,
    int SecondSourceIndex = -1,
    bool Required = false,
    string? RequiredMessage = null,
    object? Constant = null);

public sealed class MoexSeriesSpec
{
    public required string DataKind { get; init; }
    public required string Market { get; init; }
    public required string MethodTemplate { get; init; }
    public required string RootKey { get; init; }
    public required SourceColumn[] SourceColumns { get; init; }
    public required string Table { get; init; }
    public required TargetColumn[] TargetColumns { get; init; }
    public required string TokenPrefix { get; init; }
    public required PaginationKind Pagination { get; init; }

    // Код свечного интервала MOEX (1, 10, 60, 24). Заполнен только у свечей;
    // у остальных видов пуст. Диспетчеризация по нему подключается на коммите
    // исторической загрузки свечей.
    public int? CandleInterval { get; init; }

    /// <summary>
    /// У ряда на один момент времени приходится несколько строк, различающихся
    /// дополнительным измерением ключа: показателем у концентрации, типом события
    /// у мегаалертов. Порядок строк внутри такой группы источник не гарантирует,
    /// поэтому граница страницы, рассекая группу, при следующем запросе даст повтор
    /// одной строки и пропуск другой. Признак включает дочитывание группы целиком:
    /// своя страница обрезается, следующая страница начинается с его начала.
    /// У рядов с одной строкой на момент признак не нужен и остаётся выключенным.
    /// </summary>
    public bool PreserveCursorTimeGroup { get; init; }

    private string? _columnsParam;

    /// <summary>
    /// Готовое значение параметра запроса с перечнем колонок. Собирается при первом
    /// обращении и запоминается: декларации живут в статическом реестре всё время работы
    /// процесса, а сборка перечня раскодирует каждое имя из двоичного представления и
    /// склеивает результат — делать это на каждую страницу загрузки незачем.
    /// Одновременный первый доступ из двух потоков безвреден: оба соберут одинаковую
    /// строку, и запись ссылки атомарна. Замок здесь был бы платой без причины.
    /// Такое же готовое значение есть у декларации приёма — MoexRealtimeSpec.ColumnsParam.
    /// </summary>
    public string ColumnsParam => _columnsParam ??= BuildColumnsParam();

    private string BuildColumnsParam()
    {
        string[] names = new string[SourceColumns.Length];
        for (int i = 0; i < SourceColumns.Length; i++)
            names[i] = Encoding.UTF8.GetString(SourceColumns[i].Name);

        return string.Join(',', names);
    }

    private bool[]? _sourceColumnUsed;

    /// <summary>
    /// Признак востребованности колонки источника по её позиции: истина, если на позицию
    /// ссылается хоть одна целевая колонка. Значение невостребованной колонки не попадает
    /// никуда, поэтому разборщик сверяет вид её токена, но строку из неё не создаёт.
    /// Вычисляется при первом обращении и запоминается: декларации живут в статическом
    /// реестре всё время работы процесса. Одновременный первый доступ безвреден — оба
    /// потока построят одинаковый массив.
    /// </summary>
    public bool[] SourceColumnUsed => _sourceColumnUsed ??= BuildSourceColumnUsed();

    private int[]? _requiredDirectIndexes;
    private int[]? _requiredExternalSecIdIndexes;

    /// <summary>
    /// Позиции обязательных целевых колонок с прямым заполнением. Проверка обязательности
    /// раньше проходила весь перечень колонок на каждую строку данных, хотя обязательных
    /// в декларациях одна-две и позиции их постоянны. Вычисляется при первом обращении
    /// и запоминается: декларации живут в статическом реестре всё время работы процесса.
    /// </summary>
    public int[] RequiredDirectIndexes =>
        _requiredDirectIndexes ??= BuildRequiredIndexes(FillRule.Direct);

    /// <summary>Позиции обязательных целевых колонок с внешним кодом инструмента.</summary>
    public int[] RequiredExternalSecIdIndexes =>
        _requiredExternalSecIdIndexes ??= BuildRequiredIndexes(FillRule.ExternalSecId);

    private string[]? _columns;
    private Dictionary<string, string>? _columnTypes;

    /// <summary>
    /// Имена целевых колонок в порядке объявления — довод вставки в хранилище.
    /// Собирается при первом обращении и запоминается: декларации живут в статическом
    /// реестре всё время работы процесса, а перечень от диапазона к диапазону не меняется.
    /// Такие же готовые проекции есть у декларации приёма — MoexRealtimeSpec.Columns.
    /// </summary>
    public IReadOnlyList<string> Columns => _columns ??= BuildColumns();

    /// <summary>Тип хранения по имени целевой колонки — второй довод вставки.</summary>
    public IReadOnlyDictionary<string, string> ColumnTypes => _columnTypes ??= BuildColumnTypes();

    private (int Date, int Time)? _sourceTimeIndexes;

    /// <summary>
    /// Позиции колонок источника, из которых собирается момент строки. Обе равны -1,
    /// если целевой колонки с таким правилом заполнения у декларации нет. Нужны разбору
    /// для отвергнутых строк: момент такой строки иначе взять неоткуда.
    /// </summary>
    public (int Date, int Time) SourceTimeIndexes =>
        _sourceTimeIndexes ??= BuildSourceTimeIndexes();

    private string[] BuildColumns()
    {
        string[] columns = new string[TargetColumns.Length];
        for (int i = 0; i < TargetColumns.Length; i++)
            columns[i] = TargetColumns[i].Name;

        return columns;
    }

    private Dictionary<string, string> BuildColumnTypes()
    {
        Dictionary<string, string> columnTypes =
            new(TargetColumns.Length, StringComparer.Ordinal);
        for (int i = 0; i < TargetColumns.Length; i++)
            columnTypes.Add(TargetColumns[i].Name, TargetColumns[i].ColumnType);

        return columnTypes;
    }

    private (int Date, int Time) BuildSourceTimeIndexes()
    {
        for (int i = 0; i < TargetColumns.Length; i++)
        {
            if (TargetColumns[i].FillRule == FillRule.SourceDateTime)
                return (TargetColumns[i].SourceIndex, TargetColumns[i].SecondSourceIndex);
        }

        return (-1, -1);
    }

    private int[] BuildRequiredIndexes(FillRule fillRule)
    {
        int count = 0;
        for (int i = 0; i < TargetColumns.Length; i++)
        {
            if (TargetColumns[i].FillRule == fillRule && TargetColumns[i].Required)
                count++;
        }

        int[] indexes = new int[count];
        int next = 0;
        for (int i = 0; i < TargetColumns.Length; i++)
        {
            if (TargetColumns[i].FillRule == fillRule && TargetColumns[i].Required)
                indexes[next++] = i;
        }

        return indexes;
    }

    private bool[] BuildSourceColumnUsed()
    {
        bool[] used = new bool[SourceColumns.Length];
        for (int i = 0; i < TargetColumns.Length; i++)
        {
            TargetColumn target = TargetColumns[i];
            if (target.SourceIndex >= 0)
                used[target.SourceIndex] = true;
            if (target.SecondSourceIndex >= 0)
                used[target.SecondSourceIndex] = true;
        }

        return used;
    }
}
