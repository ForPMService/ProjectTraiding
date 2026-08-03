namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Постоянные метки одной производственной операции источника. Значения известны
/// до начала работы и не меняются по её ходу, поэтому набор создаётся один раз
/// в начале метода и передаётся в запись исхода по ссылке.
///
/// Переменная часть — исход и категория ошибки — в набор не входит: она известна
/// только в момент завершения.
/// </summary>
public readonly record struct MoexOperationTags(
    string Source,
    string Operation,
    string DataKind,
    string Market);
