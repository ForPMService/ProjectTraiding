using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record ManagementResultDto(
    string Operation,    // create_broker_tariff | upsert_instrument_relation
    string Target,       // moex_broker_tariffs | moex_instrument_relations
    string Status,       // ok
    long Id,           // id созданной/обновлённой строки (RETURNING id)
    int RowsWritten,
    double ElapsedMs);
    
}
