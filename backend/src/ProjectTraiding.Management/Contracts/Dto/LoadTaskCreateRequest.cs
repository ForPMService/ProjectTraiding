using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>
    /// Параметры создания задачи загрузки, приходящие с фронта.
    /// candle_interval обязателен для candles, иначе null.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record LoadTaskCreateRequest(
        string Secid,
        string Market,
        string Boardid,
        string DataKind,
        int? CandleInterval,
        DateOnly DateFrom,
        DateOnly DateTill,
        string StorageTarget);
}
