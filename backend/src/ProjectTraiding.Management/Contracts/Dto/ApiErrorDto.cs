namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>
    /// Тело ответа на необработанное исключение. Ни текст исключения, ни след стека
    /// наружу не отдаются: только код состояния, короткое описание и идентификатор
    /// запроса, по которому событие находится в журнале.
    /// </summary>
    public sealed record ApiErrorDto(
        int Status,
        string Title,
        string? TraceId);
}
