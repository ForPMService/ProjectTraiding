namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>Подтверждение постановки заявки удаления в очередь владельца данных.</summary>
    public sealed record InstrumentDataDeleteAcceptedResponse(
        string Secid,
        Guid DeletionId,
        string Status);
}
