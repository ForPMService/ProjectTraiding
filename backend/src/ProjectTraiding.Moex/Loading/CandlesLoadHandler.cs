using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;
using ProjectTraiding.Moex.Infrastructure.RawCapture;
using ProjectTraiding.Moex.StorageBase.ClickHouse;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Обработчик свечей. Один метод клиента на оба рынка, адрес различается регистром доски.
    /// Пока обслуживает минутные свечи (целевая таблица moex_candles_1m); другие интервалы —
    /// отдельными картами и обработчиками на следующем шаге.
    /// </summary>
    public sealed class CandlesLoadHandler : ILoadHandler
    {
        private readonly MoexHttpAlgClient _client;
        private readonly RowWriter<CandlesDTO> _writer;

        public CandlesLoadHandler(MoexHttpAlgClient client, RowWriter<CandlesDTO> writer)
        {
            _client = client;
            _writer = writer;
        }

        public bool CanHandle(MoexLoadTask task) =>
            task.DataKind == "candles"
            && (task.Market == "stock" || task.Market == "futures");

        public async Task<CandlesWriteSummary> LoadAsync(
            MoexLoadTask task, LoadStopOutcome stopOutcome, CancellationToken ct)
        {
            string method = BuildMethod(task);
            Dictionary<string, string> query = BuildQuery(task);
            string captureMarket = task.Market == "stock"
                ? RawCaptureMarkets.Stock
                : RawCaptureMarkets.Futures;

            IAsyncEnumerable<List<CandlesDTO>> pages = _client.GetCandles(
                method, query, captureMarket: captureMarket, secid: task.Secid,
                stopOutcome: stopOutcome, cancellationToken: ct);

            return await _writer.WriteRangeAsync(
                task.Secid, task.SourceContractVersion, task.WriterVersion, pages, ct);
        }

        // Доска в адресе в РАЗНОМ регистре: у акций строчными (boards/tqbr),
        // у фьючерсов прописными (boards/RFUD) — форма, проверенная диагностикой.
        private static string BuildMethod(MoexLoadTask task)
        {
            if (task.Market == "stock")
            {
                string board = task.Boardid.ToLowerInvariant();
                return $"/engines/stock/markets/shares/boards/{board}/securities/{task.Secid}/candles.json";
            }
            else
            {
                string board = task.Boardid.ToUpperInvariant();
                return $"/engines/futures/markets/forts/boards/{board}/securities/{task.Secid}/candles.json";
            }
        }

        private static Dictionary<string, string> BuildQuery(MoexLoadTask task)
        {
            return new Dictionary<string, string>
            {
                ["interval"] = task.CandleInterval!.Value.ToString(CultureInfo.InvariantCulture),
                ["from"] = task.DateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["till"] = task.DateTill.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
        }
    }
}
