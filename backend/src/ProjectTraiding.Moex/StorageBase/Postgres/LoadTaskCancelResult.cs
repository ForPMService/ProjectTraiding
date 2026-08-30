namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public readonly record struct LoadTaskCancelResult(
        int CancelledCount,
        int CancelRequestedCount,
        TimeSpan Elapsed);
}
