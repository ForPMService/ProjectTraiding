namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Значения метки телеметрии <c>outcome</c>. Исход отвечает на вопрос «чем кончилось»
/// и намеренно содержит ровно три значения: конкретная причина отказа передаётся
/// отдельной меткой <c>error_type</c>, а не размножением исходов.
///
/// Это не статусы задачи загрузки в PostgreSQL: у тех своё множество значений
/// и своя таблица переходов.
/// </summary>
public static class MoexOutcomes
{
    /// <summary>Работа выполнена, включая корректный пустой результат.</summary>
    public const string Success = "success";

    /// <summary>Работа прервана отказом; причина — в метке категории ошибки.</summary>
    public const string Error = "error";

    /// <summary>Работа прервана отменой вызывающей стороны или остановкой хоста.</summary>
    public const string Cancelled = "cancelled";
}
