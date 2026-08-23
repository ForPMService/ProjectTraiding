using System.Diagnostics;

namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Единая точка входа для телеметрии контура MOEX.
/// Константы имён регистрируются в хосте через AddProjectTraidingObservability.
/// ActivitySource используется для создания доменных Activity в клиентах и пагинации.
///
/// Будущие модули повторяют паттерн: StorageTelemetry, DataQualityTelemetry.
/// </summary>
public static class MoexTelemetry
{
    public const string ActivitySourceName = "ProjectTraiding.Moex";
    public const string MeterName = "ProjectTraiding.Moex";

    /// <summary>
    /// Общее начало имён всех отрезков приёма реального времени. Приём — ровный фоновый
    /// цикл без ветвлений, разбирать который трассировкой нечего: наблюдение за ним ведётся
    /// метриками и журналом. Хост отдаёт этот префикс сэмплеру, и поддерево приёма в Tempo
    /// не уходит. Вернуть приём в трассировку — убрать константу из вызова в Program.cs.
    /// Метрики приёма это имя не затрагивает, хотя и начинаются так же: конвейер метрик
    /// сэмплера не имеет.
    /// </summary>
    public const string RealtimeActivityNamePrefix = "moex.realtime.";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
