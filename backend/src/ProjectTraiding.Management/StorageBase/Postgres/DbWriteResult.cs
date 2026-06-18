using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public readonly record struct DbWriteResult(
        string Table,
        int InputCount,
        int RowsWritten,
        TimeSpan Elapsed);
    
}
