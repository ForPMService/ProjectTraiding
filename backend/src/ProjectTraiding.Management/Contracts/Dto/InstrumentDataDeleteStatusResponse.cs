namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>Последнее состояние заявки удаления данных инструмента.</summary>
    public sealed record InstrumentDataDeleteStatusResponse(
        string Secid,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset NextAttemptAt,
        string? ErrorMessage);
}
