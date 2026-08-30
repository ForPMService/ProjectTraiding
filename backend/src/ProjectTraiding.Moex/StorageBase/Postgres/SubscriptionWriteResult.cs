namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public readonly record struct SubscriptionWriteResult(int RowsWritten, TimeSpan Elapsed);
}
