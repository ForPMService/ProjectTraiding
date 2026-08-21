namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>
    /// Итог удаления рабочих данных инструмента. Отдаётся только при успехе;
    /// отказы уходят кодом состояния и текстом, без этого DTO.
    /// </summary>
    public sealed record InstrumentDataDeleteResponse(
        string Secid,
        string Status,
        int LoadedRangesDeleted,
        int StreamCursorsDeleted,
        int LoadTasksDeleted,
        int ClickHouseTablesCleared,
        double ElapsedMs);
}
