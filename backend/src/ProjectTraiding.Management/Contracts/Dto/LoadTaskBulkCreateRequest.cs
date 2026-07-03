using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record LoadTaskBulkCreateRequest(
        IReadOnlyList<LoadTaskBulkInstrumentRequest> Instruments,
        string[] StockDataKinds,
        string[] FuturesDataKinds,
        int[] CandleIntervals,
        string StorageTarget,
        int? SliceWeeks);
}
