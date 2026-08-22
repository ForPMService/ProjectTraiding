using System;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    /// <summary>
    /// Итог одной операции записи контура управления. Имя не совпадает с типом
    /// контура биржи намеренно: прежнее совпадение имён в почти одинаковых
    /// пространствах имён приводило к тому, что один оператор импорта молча менял
    /// тип переменной.
    ///
    /// Идентификатор отсутствует у операций, не создающих сущность с ключом —
    /// например, у включения и отключения подписок.
    /// </summary>
    public readonly record struct ManagementWriteResult(
        long? Id,
        int RowsWritten,
        TimeSpan Elapsed);
}
