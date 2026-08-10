using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.Telemetry;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Globalization;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Обобщённый обработчик видов данных одной формы «диапазон дат → поток страниц → писатель».
    /// Закрывается парой «паспорт × тип строки»; ограничение по структуре гарантирует, что при
    /// нативной компиляции каждая пара порождает собственную специализацию с прямыми
    /// статическими вызовами — машинный код эквивалентен прежним штучным обработчикам.
    /// Свечи и FUTOI остаются отдельными обработчиками: у них иная форма загрузки.
    /// </summary>
    public sealed class SeriesLoadHandler<TKind, TRow> : ILoadHandler
        where TKind : struct, IAlgCursorKind<TRow>
    {
        private readonly MoexHttpAlgClient _client;
        private readonly RowWriter<TRow> _writer;

        public SeriesLoadHandler(MoexHttpAlgClient client, RowWriter<TRow> writer)
        {
            _client = client;
            _writer = writer;
        }

        public bool CanHandle(MoexLoadTask task) =>
            task.DataKind == TKind.DataKind && task.Market == TKind.Market;

        public async Task<RowWriteSummary> LoadAsync(
            MoexLoadTask task, LoadStopOutcome stopOutcome, CancellationToken ct)
        {
            string method = TKind.BuildMethod(task.Secid);
            Dictionary<string, string> query = BuildQuery(task);

            MoexOperationTags operationTags = new MoexOperationTags(
                MoexLogSources.Algopack,
                MoexOperations.HistoryCursorFetch,
                TKind.TelemetryDataKind,
                TKind.TelemetryMarket,
                MoexFlows.History);

            IAsyncEnumerable<List<TRow>> pages = _client.GetCursorPages<TKind, TRow>(
                method, query,
                stopOutcome: stopOutcome, operationTags: operationTags, cancellationToken: ct);

            StorageInsertContext insertContext = new StorageInsertContext(
                operationTags.DataKind,
                operationTags.Market,
                MoexFlows.History);

            return await _writer.WriteRangeAsync(
                task.Id, task.Secid, task.SourceContractVersion, task.WriterVersion,
                pages, insertContext, ct);
        }

        private static Dictionary<string, string> BuildQuery(MoexLoadTask task)
        {
            return new Dictionary<string, string>
            {
                ["from"] = task.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["till"] = task.DateTill.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
        }
    }
}
