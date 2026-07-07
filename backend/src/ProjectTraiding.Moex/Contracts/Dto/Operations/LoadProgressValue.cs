using System;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Moex.Contracts.Dto.Operations
{
    /// <summary>
    /// Значение живого прогресса одной задачи загрузки в оперативном хранилище.
    /// Пишется на каждом сбросе пачки. В базе истины прогресса нет — это первичное
    /// место значения. Несёт отметку получения и признак устаревания, как предписано
    /// для горячих значений; читающая сторона витрины не показывает устаревшее как свежее.
    /// </summary>
    public sealed record LoadProgressValue(
        [property: JsonPropertyName("rowsRead")] long RowsRead,
        [property: JsonPropertyName("lastSourceTime")] DateTimeOffset LastSourceTime,
        [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
        [property: JsonPropertyName("receivedAt")] DateTimeOffset ReceivedAt,
        [property: JsonPropertyName("isStale")] bool IsStale);
}
