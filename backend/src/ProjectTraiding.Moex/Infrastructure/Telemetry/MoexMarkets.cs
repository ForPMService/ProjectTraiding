namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Значения метки телеметрии <c>market</c>. Это не виды внутренних задач
/// <c>MoexLoadTask.DataKind</c> и не имена корневых блоков ответа источника;
/// совпадение отдельных строковых значений не означает тождество понятий.
/// </summary>
public static class MoexMarkets
{
    public const string Stock = "stock";
    public const string Futures = "futures";
}
