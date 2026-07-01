using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public readonly record struct BulkCreateResult(
        int ExpandedCount,
        int InsertedCount,
        int SkippedDuplicateCount);
}
