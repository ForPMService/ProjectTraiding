namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>
/// Разобранная страница приёма сделок или стакана. Один объект на страницу, не на строку.
/// Границы пачки и хвостовые значения вычисляются тем же проходом, что и строки, поэтому
/// служба не обращается к строкам по номеру позиции.
///
/// Даты торговой сессии здесь нет намеренно: она нужна только самому разбору, который уже
/// проставил её в строки, и ни один потребитель за пределами разбора её не читает.
/// </summary>
public sealed class RealtimeParsedPage
{
    public RealtimeParsedPage(
        List<object?[]> rows,
        DateTime firstTime,
        DateTime lastTime,
        long? lastTradeNo,
        long? maxTradeNo,
        DateTime? maxTradeNoTime,
        Exception? maxTradeNoTimeFailure,
        Exception? guardFailure)
    {
        Rows = rows;
        FirstTime = firstTime;
        LastTime = lastTime;
        LastTradeNo = lastTradeNo;
        MaxTradeNo = maxTradeNo;
        MaxTradeNoTime = maxTradeNoTime;
        MaxTradeNoTimeFailure = maxTradeNoTimeFailure;
        GuardFailure = guardFailure;
    }

    /// <summary>Строки вставки в той форме, которую принимает исполнитель вставки.</summary>
    public List<object?[]> Rows { get; }

    /// <summary>Ключевое время первой строки. При пустой странице не определено.</summary>
    public DateTime FirstTime { get; }

    /// <summary>Ключевое время последней строки.</summary>
    public DateTime LastTime { get; }

    /// <summary>Номер сделки последней строки. У видов данных без номера — пусто.</summary>
    public long? LastTradeNo { get; }

    /// <summary>
    /// Наибольший непустой номер сделки на странице. Нужен единственному потребителю —
    /// холодному старту, который встаёт на хвост по наибольшему номеру и пропускает строки
    /// с пустым номером. Обычный оборот этим значением не пользуется.
    /// </summary>
    public long? MaxTradeNo { get; }

    /// <summary>
    /// Ключевое время строки с наибольшим номером сделки. Пусто, если у этой строки момент
    /// собрать не удалось. Различие существенно: нынешний холодный старт строку без номера
    /// пропускает молча, но на выбранной строке с невосстановимым временем отказывает.
    /// </summary>
    public DateTime? MaxTradeNoTime { get; }

    /// <summary>
    /// Отказ, случившийся при сборке момента у строки с наибольшим номером сделки, или пусто.
    /// Хранится отдельно от общего признака: сегодня отбор хвоста вызывает сборку момента
    /// только на выбранной строке, и причина отказа именно её. Причин две и они различны —
    /// пустые дата или время дают один отказ, непустые неверного формата — другой.
    /// </summary>
    public Exception? MaxTradeNoTimeFailure { get; }

    /// <summary>
    /// Первое нарушение построчного стража на странице или пусто. Хранится самим исключением,
    /// а не его текстом: тип отказа виден в записях журнала, и заворачивание текста в новое
    /// исключение подменило бы его. Разборщик не бросает сам — отказ обязан выйти наружу
    /// после того, как учтены успех получения и число полученных строк. Решение о броске
    /// принимает служба, уже в фазе фиксации.
    /// </summary>
    public Exception? GuardFailure { get; }
}
