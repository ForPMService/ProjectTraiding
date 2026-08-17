using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Итог записи диапазона (любого вида данных, не только свечей).
    /// RowsRead — покрытый объём (детерминированный счёт прочитанных строк), идёт в rows_total.
    /// LastToken — токен последней пачки (аудиторный след).
    /// </summary>
    public readonly record struct RowWriteSummary(
        long RowsRead,
        string? LastToken);
}
