using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Итог пакетной постановки.
    ///
    /// Непустой BlockedSecids означает, что пакет отклонён целиком: по перечисленным
    /// инструментам идёт удаление данных, не вставлено ни одного задания,
    /// InsertedCount и SkippedDuplicateCount равны нулю.
    ///
    /// Пустой BlockedSecids означает успех, в том числе частичный по дублям:
    /// SkippedDuplicateCount считает задания, отброшенные как повторы уже активных.
    /// Это штатный исход постановки, а не отказ.
    /// </summary>
    public readonly record struct BulkCreateResult(
        int ExpandedCount,
        int InsertedCount,
        int SkippedDuplicateCount,
        IReadOnlyList<string> BlockedSecids);
}
