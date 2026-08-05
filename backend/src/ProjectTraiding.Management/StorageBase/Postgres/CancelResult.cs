using System;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public readonly record struct CancelResult(
        int CancelledCount,
        TimeSpan Elapsed);
}
