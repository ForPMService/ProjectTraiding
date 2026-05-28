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

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
