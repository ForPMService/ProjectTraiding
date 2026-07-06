using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using ProjectTraiding.Moex.Contracts.Pagination;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Паспорта десяти видов данных с одинаковой формой загрузки. Каждый паспорт — структура
    /// без состояния и без экземпляров: она существует только как параметр типа обобщённого
    /// обработчика. Строковые значения и привязки к методам клиента перенесены из удалённых
    /// штучных обработчиков дословно.
    /// </summary>
    public readonly struct TradeStatsStockKind : ILoadKind<SuperCandlesTradeStats5mDTO>
    {
        public static string DataKind => "tradestats";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/tradestats/{secid}.json";

        public static IAsyncEnumerable<List<SuperCandlesTradeStats5mDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetSuperCandlesTradeStats5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct TradeStatsFuturesKind : ILoadKind<SuperCandlesFuturesTradeStats5mDTO>
    {
        public static string DataKind => "tradestats";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/tradestats/{secid}.json";

        public static IAsyncEnumerable<List<SuperCandlesFuturesTradeStats5mDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetSuperCandlesFuturesTradeStats5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct ObStatsStockKind : ILoadKind<SuperCandlesOrderBookStats5mDTO>
    {
        public static string DataKind => "obstats";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/obstats/{secid}.json";

        public static IAsyncEnumerable<List<SuperCandlesOrderBookStats5mDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetSuperCandlesOrderBookStats5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct ObStatsFuturesKind : ILoadKind<SuperCandlesFuturesOrderBookStats5mDTO>
    {
        public static string DataKind => "obstats";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/obstats/{secid}.json";

        public static IAsyncEnumerable<List<SuperCandlesFuturesOrderBookStats5mDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetSuperCandlesFuturesOrderBookStats5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct OrderStatsStockKind : ILoadKind<SuperCandlesOrderStats5mDTO>
    {
        public static string DataKind => "orderstats";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/orderstats/{secid}.json";

        public static IAsyncEnumerable<List<SuperCandlesOrderStats5mDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetSuperCandlesOrderStats5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct FutoiKind : ILoadKind<FutoiDTO>
    {
        public static string DataKind => "futoi";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/analyticalproducts/futoi/securities/{secid}.json";

        public static IAsyncEnumerable<List<FutoiDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.StreamFutoi(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct Hi2StockKind : ILoadKind<Hi2AssetDTO>
    {
        public static string DataKind => "hi2";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/hi2/{secid}.json";

        public static IAsyncEnumerable<List<Hi2AssetDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetHi2Asset5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct Hi2FuturesKind : ILoadKind<Hi2FuturesDTO>
    {
        public static string DataKind => "hi2";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/hi2/{secid}.json";

        public static IAsyncEnumerable<List<Hi2FuturesDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetHi2Futures5m(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct MegaAlertsStockKind : ILoadKind<MegaAlertsAssetsDTO>
    {
        public static string DataKind => "mega_alerts";
        public static string Market => "stock";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/eq/alerts/{secid}.json";

        public static IAsyncEnumerable<List<MegaAlertsAssetsDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetMegaAlerts(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }

    public readonly struct MegaAlertsFuturesKind : ILoadKind<MegaAlertsFuturesDTO>
    {
        public static string DataKind => "mega_alerts";
        public static string Market => "futures";

        public static string BuildMethod(string secid) =>
            $"/datashop/algopack/fo/alerts/{secid}.json";

        public static IAsyncEnumerable<List<MegaAlertsFuturesDTO>> GetPages(
            MoexHttpAlgClient client, string method, Dictionary<string, string> query,
            string runId, string secid, LoadStopOutcome stopOutcome, CancellationToken ct)
            => client.GetMegaAlertsFutures(
                method, query, runId: runId, secid: secid,
                stopOutcome: stopOutcome, cancellationToken: ct);
    }
}
