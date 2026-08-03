using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse;

/// <summary>
/// Разрез вставки для телеметрии: что именно и откуда пишется. Контекст приходит
/// от владельца операции — обработчика загрузки или приёмника, — потому что только
/// он знает вид данных, рынок и происхождение потока.
///
/// Карта строк этих сведений не несёт и нести не должна: она описывает форму вставки,
/// а не её происхождение. Одна и та же карта обслуживает и историческую загрузку,
/// и текущий приём, поэтому вывести поток из карты невозможно.
/// </summary>
public readonly record struct StorageInsertContext(
    string DataKind,
    string Market,
    string Flow);
