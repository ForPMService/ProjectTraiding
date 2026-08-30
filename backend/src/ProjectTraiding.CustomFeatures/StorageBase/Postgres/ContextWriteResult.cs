namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public readonly record struct ContextWriteResult(long? Id, int RowsWritten, TimeSpan Elapsed);
}
