using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Итог записи диапазона.
    /// RowsRead — покрытый объём (детерминированный счёт прочитанных свечей), идёт в rows_total.
    /// RowsInsertedReported — что вернул драйвер; при повторе дедуплицированная пачка вернёт меньше,
    ///   поэтому это число только для журнала.
    /// LastToken — токен последней пачки (аудиторный след).
    /// </summary>
    public readonly record struct CandlesWriteSummary(
        long RowsRead,
        long RowsInsertedReported,
        string? LastToken);
}
