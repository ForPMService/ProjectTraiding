using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Globalization;

namespace ProjectTraiding.Moex.Loading
{
    public sealed class OrderStatsStockLoadHandler : ILoadHandler
    {
        private readonly MoexHttpAlgClient _client;
        private readonly RowWriter<SuperCandlesOrderStats5mDTO> _writer;

        public OrderStatsStockLoadHandler(
            MoexHttpAlgClient client,
            RowWriter<SuperCandlesOrderStats5mDTO> writer)
        {
            _client = client;
            _writer = writer;
        }

        public bool CanHandle(MoexLoadTask task) =>
            task.DataKind == "orderstats" && task.Market == "stock";

        public async Task<RowWriteSummary> LoadAsync(
            MoexLoadTask task, LoadStopOutcome stopOutcome, CancellationToken ct)
        {
            string method = BuildMethod(task);
            Dictionary<string, string> query = BuildQuery(task);

            IAsyncEnumerable<List<SuperCandlesOrderStats5mDTO>> pages =
                _client.GetSuperCandlesOrderStats5m(
                    method, query, runId: task.Id.ToString("N"), secid: task.Secid,
                    stopOutcome: stopOutcome, cancellationToken: ct);

            return await _writer.WriteRangeAsync(
                task.Secid, task.SourceContractVersion, task.WriterVersion, pages, ct);
        }

        private static string BuildMethod(MoexLoadTask task) =>
            $"/datashop/algopack/eq/orderstats/{task.Secid}.json";

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
