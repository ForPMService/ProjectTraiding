using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    // Одна строка таблицы moex_loaded_ranges. Плоский список: группировку по виду
    // данных при необходимости делает фронт, в контракт она не зашивается.
    public sealed record VitrineLoadedRangeDto(
        long Id,
        string Secid,
        string Market,
        string Boardid,
        string DataKind,
        int? CandleInterval,          // осмыслен только для свечей, иначе null
        DateOnly DateFrom,
        DateOnly DateTill,
        long RowsTotal,
        string Status,                // ok, partial, stale
        string StorageTarget,         // none, file, clickhouse
        DateTimeOffset LastSuccessAt);
}
