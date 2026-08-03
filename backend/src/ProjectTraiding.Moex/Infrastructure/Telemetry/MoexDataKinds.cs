namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Значения метки телеметрии <c>data_kind</c>. Это не виды внутренних задач
/// <c>MoexLoadTask.DataKind</c> и не имена корневых блоков ответа источника;
/// совпадение отдельных строковых значений не означает тождество понятий.
/// </summary>
public static class MoexDataKinds
{
    public const string Candles = "candles";
    public const string TradeStats = "tradestats";
    public const string OBStats = "obstats";
    public const string OrderStats = "orderstats";
    public const string Futoi = "futoi";
    public const string Hi2 = "hi2";
    public const string MegaAlerts = "alerts";
    public const string OffDays = "offdays";
    public const string Trades = "trades";
    public const string Orderbook = "orderbook";
    public const string Instruments = "instruments";

    /// <summary>
    /// Сопоставляет вид задачи загрузки (<c>moex_load_tasks.data_kind</c>) со значением
    /// метки телеметрии. Значения совпадают дословно для всех видов, кроме оповещений:
    /// в задаче записано "mega_alerts", в телеметрии принято "alerts". Проверено по всем
    /// девяти паспортам курсорных видов.
    ///
    /// Метод существует именно ради этого единственного расхождения: без него метрики
    /// заданий и метрики полученных строк нельзя сопоставить по оповещениям.
    /// </summary>
    /// <param name="taskDataKind">Вид данных задачи загрузки.</param>
    public static string FromTaskDataKind(string taskDataKind)
    {
        if (taskDataKind == "mega_alerts")
            return MegaAlerts;

        return taskDataKind;
    }
}
