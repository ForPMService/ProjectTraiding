namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Значения метки телеметрии <c>flow</c> — происхождение потока данных.
/// Метка позволяет сравнивать полученное и записанное отдельно для исторической
/// загрузки и для текущего приёма: у них разный темп, разные объёмы и разные
/// нормальные значения, и общая цифра по обоим потокам не значит ничего.
/// </summary>
public static class MoexFlows
{
    /// <summary>Историческая загрузка по задаче оператора.</summary>
    public const string History = "history";

    /// <summary>Текущий приём по подписке инструмента.</summary>
    public const string Realtime = "realtime";
}
