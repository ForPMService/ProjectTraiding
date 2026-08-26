using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Задача загрузки в части, нужной для исполнения: что грузить и с какими версиями.
    /// Поля-результаты пишет жизненный цикл, здесь их нет.
    /// </summary>
    public sealed record MoexLoadTask(
        Guid Id,
        string Secid,
        string Market,
        string Boardid,
        string DataKind,
        int? CandleInterval,
        DateOnly DateFrom,
        DateOnly DateTill,
        string StorageTarget);
}
