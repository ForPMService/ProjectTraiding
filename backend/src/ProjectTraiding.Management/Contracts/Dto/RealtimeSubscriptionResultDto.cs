namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record RealtimeSubscriptionResultDto(
        string Operation,
        string Secid,
        string DataKind,
        bool Enabled,
        int RowsWritten,
        double ElapsedMs);
}
