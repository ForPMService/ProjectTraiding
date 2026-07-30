using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Globalization;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Обработчик FUTOI. Источник не поддерживает курсорную пагинацию, поэтому клиент
    /// разбивает диапазон по дням и загружает каждый день отдельным запросом.
    /// </summary>
    public sealed class FutoiLoadHandler : ILoadHandler
    {
        private readonly MoexHttpAlgClient _client;
        private readonly RowWriter<FutoiDTO> _writer;

        public FutoiLoadHandler(MoexHttpAlgClient client, RowWriter<FutoiDTO> writer)
        {
            _client = client;
            _writer = writer;
        }

        public bool CanHandle(MoexLoadTask task) =>
            task.DataKind == "futoi" && task.Market == "futures";

        private static bool IsPerpetual(string secid)
        {
            return secid is "USDRUBF"
                or "EURRUBF"
                or "CNYRUBF"
                or "IMOEXF"
                or "GLDRUBF"
                or "SBERF"
                or "GAZPF";
        }

        private static string ResolveRequestKey(MoexLoadTask task)
        {
            if (IsPerpetual(task.Secid))
                return task.Secid;

            if (string.IsNullOrWhiteSpace(task.SecType))
            {
                throw new InvalidOperationException(
                    $"Открытый интерес требует код серии контракта; " +
                    $"у инструмента {task.Secid} SECTYPE не заполнен.");
            }

            return task.SecType;
        }

        public async Task<RowWriteSummary> LoadAsync(
            MoexLoadTask task, LoadStopOutcome stopOutcome, ILoadProgressReporter progress, CancellationToken ct)
        {
            string requestKey = ResolveRequestKey(task);

            string method =
                $"/analyticalproducts/futoi/securities/{requestKey}.json";
            Dictionary<string, string> query = new Dictionary<string, string>
            {
                ["from"] = task.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["till"] = task.DateTill.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };

            IAsyncEnumerable<List<FutoiDTO>> pages = _client.StreamFutoi(
                method, query,
                stopOutcome: stopOutcome, cancellationToken: ct);

            return await _writer.WriteRangeAsync(
                task.Id, task.Secid, task.SourceContractVersion, task.WriterVersion, pages, progress, ct);
        }
    }
}
