using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>
    /// Единая политика сердцебиения сеанса приёма. Одинакова для сделок, стакана
    /// и свечей: если минимальный интервал ещё не прошёл — ничего не делать,
    /// иначе записать сердцебиение и только после успешной записи сдвинуть отметку.
    ///
    /// Порядок существенен: при исключении записи отметка не двигается, и следующий
    /// оборот повторит попытку. Менять его местами — значит терять сердцебиение молча.
    ///
    /// Политика намеренно вынесена отдельно от RealtimeReceiverServiceBase: класс
    /// внешнего жизненного цикла не должен знать ни писателя покрытия, ни состояния
    /// инструмента.
    /// </summary>
    internal static class ReceiverSessionHeartbeat
    {
        public static async Task WriteIfDueAsync(
            ReceiverInstrumentSessionState state,
            TimeSpan minInterval,
            StreamCoverageWriter coverageWriter,
            CancellationToken ct)
        {
            if (Stopwatch.GetElapsedTime(state.LastHeartbeatTimestamp) < minInterval)
                return;

            await coverageWriter.HeartbeatAsync(state.SessionId, state.RowsTotal, ct);
            state.LastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        }
    }
}
