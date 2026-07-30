namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Значения метки телеметрии <c>data_kind</c>. Это не виды внутренних задач
/// <c>MoexLoadTask.DataKind</c>, не имена корневых блоков ответа источника и не ключи
/// хранилища; совпадение отдельных строковых значений не означает тождество понятий.
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
}
