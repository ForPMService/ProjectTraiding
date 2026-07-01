using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record LoadTaskBulkInstrumentRequest(
        string Secid,
        string Market,
        string Boardid,
        DateOnly DateFrom,
        DateOnly DateTill);
}
