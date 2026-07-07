using System;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Пустая реализация приёмника хода загрузки: метод исполняется, но ничего не делает.
    /// Временная заглушка шага 1 — механизм отдачи собран и работает, поведение не меняется.
    /// Шаг 2 заменит её на писателя прогресса в оперативное хранилище.
    /// </summary>
    public sealed class NoOpLoadProgressReporter : ILoadProgressReporter
    {
        public Task ReportAsync(Guid taskId, long rowsRead, DateTime lastSourceTime, CancellationToken ct)
            => Task.CompletedTask;
    }
}
