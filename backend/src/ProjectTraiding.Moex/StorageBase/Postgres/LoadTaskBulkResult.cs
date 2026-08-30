namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public readonly record struct LoadTaskBulkResult(
        int ExpandedCount,
        int InsertedCount,
        int SkippedDuplicateCount,
        IReadOnlyList<string> BlockedSecids);
}
