using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record LoadTaskBulkCreateResponse(
        int ExpandedCount,
        int InsertedCount,
        int SkippedDuplicateCount,
        int? SliceWeeks);
}
