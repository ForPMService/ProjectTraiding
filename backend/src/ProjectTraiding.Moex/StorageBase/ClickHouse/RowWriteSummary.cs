using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Итог записи диапазона (любого вида данных, не только свечей).
    /// RowsRead — покрытый объём (детерминированный счёт прочитанных строк), идёт в rows_total.
    /// RowsSkipped — число строк, отвергнутых разбором; идёт в rows_skipped покрытия.
    /// </summary>
    public readonly record struct RowWriteSummary(
        long RowsRead,
        long RowsSkipped);
}
