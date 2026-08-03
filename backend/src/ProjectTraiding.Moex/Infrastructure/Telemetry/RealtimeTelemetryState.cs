using System.Collections.Concurrent;

namespace ProjectTraiding.Moex.Infrastructure.Telemetry;

/// <summary>
/// Ключ агрегата телеметрии текущего приёма. Ограничен видом данных и рынком:
/// инструмент в ключ не входит, иначе число рядов метрики росло бы вместе
/// с числом подписок.
/// </summary>
public readonly record struct RealtimeTelemetryKey(string DataKind, string Market);

/// <summary>
/// Готовые скаляры одного разреза текущего приёма, посчитанные приёмником
/// в конце оборота. Обработчик наблюдаемой метрики читает только их и не
/// обращается ни к словарям состояний, ни к базе.
/// </summary>
public readonly record struct RealtimeTelemetrySnapshot(
    DateTimeOffset? LastSuccessfulPollTime,
    DateTimeOffset? LastConfirmedMarketTime,
    long ActiveInstruments,
    long StaleInstruments);

/// <summary>
/// Владелец агрегированного состояния телеметрии текущего приёма. Пишут в него
/// три приёмника в конце своих оборотов, читает обработчик наблюдаемых метрик
/// из потока экспортёра — поэтому хранилище потокобезопасно по конструкции.
///
/// Состояние статическое, потому что обработчики наблюдаемых метрик создаются
/// один раз вместе с инструментами и до экземпляров приёмников не дотягиваются.
/// Разрезов немного и они не растут: вид данных на рынок.
/// </summary>
public static class RealtimeTelemetryState
{
    private static readonly ConcurrentDictionary<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> Snapshots =
        new ConcurrentDictionary<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>();

    /// <summary>
    /// Заменяет состояние всех разрезов одного вида данных. Разрезы, отсутствующие
    /// в переданном наборе, удаляются: иначе после снятия последней подписки рынка
    /// метрика продолжала бы отдавать давно неактуальный агрегат.
    ///
    /// Дополнительной синхронизации между приёмниками не требуется: каждый вид данных
    /// имеет единственного писателя — свою фоновую службу.
    /// </summary>
    public static void ReplaceForDataKind(
        string dataKind,
        IReadOnlyCollection<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> snapshots)
    {
        foreach (KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> pair in snapshots)
        {
            Snapshots[pair.Key] = pair.Value;
        }

        // Удаление отсутствующих: перебор ключей потокобезопасного словаря
        // безопасен и во время изменения.
        foreach (RealtimeTelemetryKey key in Snapshots.Keys)
        {
            if (key.DataKind != dataKind)
                continue;

            bool present = false;
            foreach (KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot> pair in snapshots)
            {
                if (pair.Key.Equals(key))
                {
                    present = true;
                    break;
                }
            }

            if (!present)
                Snapshots.TryRemove(key, out _);
        }
    }

    /// <summary>Перечисляет опубликованные разрезы для обработчиков наблюдаемых метрик.</summary>
    public static IEnumerable<KeyValuePair<RealtimeTelemetryKey, RealtimeTelemetrySnapshot>> Enumerate()
    {
        return Snapshots;
    }
}
