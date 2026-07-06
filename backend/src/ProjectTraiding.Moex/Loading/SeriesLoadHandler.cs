using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Pagination;
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
    /// Свечи остаются штучным обработчиком: у них выбор писателя по интервалу и иная форма.
    /// </summary>
    public sealed class SeriesLoadHandler<TKind, TRow> : ILoadHandler
        where TKind : struct, ILoadKind<TRow>
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

            IAsyncEnumerable<List<TRow>> pages = TKind.GetPages(
                _client, method, query,
                runId: task.Id.ToString("N"), secid: task.Secid,
                stopOutcome: stopOutcome, ct: ct);

            return await _writer.WriteRangeAsync(
                task.Secid, task.SourceContractVersion, task.WriterVersion, pages, ct);
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
