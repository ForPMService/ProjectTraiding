using System;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    // RowsWritten — число успешно обработанных логических сущностей,
    // не число SQL-команд.
    public readonly record struct DbWriteResult(
        int InputCount,
        int RowsWritten,
        TimeSpan Elapsed);
}
