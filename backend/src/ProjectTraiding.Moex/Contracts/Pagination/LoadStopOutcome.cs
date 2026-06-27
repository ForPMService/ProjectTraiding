using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Contracts.Pagination
{
    /// <summary>
    /// Держатель причины остановки загрузки. Поток клиента (IAsyncEnumerable) не может вернуть
    /// причину выходным параметром, поэтому координатор создаёт держатель, передаёт его в метод
    /// клиента, а тот заполняет причину в точке остановки. Координатор читает держатель после
    /// того, как поток полностью прочитан писателем.
    /// </summary>
    public sealed class LoadStopOutcome
    {
        public string? StopReason { get; private set; }
        public bool IsPartial { get; private set; }

        public void Complete(string reason, bool isPartial)
        {
            StopReason = reason;
            IsPartial = isPartial;
        }
    }
}
