namespace ProjectTraiding.Diagnostics.Probe;

/// <summary>
/// Источники журнала диагностического проекта. Значение потокового источника перенесено
/// из внутреннего реестра контура Moex вместе со всем слоем потокового соединения:
/// боевой путь текущих данных построен на опросе через REST, а потоковое соединение
/// остаётся разведочной веткой и контуру Moex больше не принадлежит.
/// </summary>
internal static class DiagnosticsLogSources
{
    public const string WebSocket = "MOEX_WEBSOCKET";
}
