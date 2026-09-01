namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres;

public readonly record struct CalendarBulkWriteResult(
    int InputCount,
    int RowsWritten,
    TimeSpan Elapsed);
