using System.Collections.Immutable;

namespace ProjectTraiding.Moex.DataFormats;

/// <summary>
/// Ordered input column names for the dense MOEX market formats parsed by this project.
/// </summary>
public static class MoexColumnSets
{
    public static readonly ImmutableArray<string> TradeStatsStock =
    [
        "tradedate", "tradetime", "secid", "pr_open", "pr_high", "pr_low", "pr_close",
        "pr_std", "vol", "val", "trades", "pr_vwap", "pr_change", "trades_b", "trades_s",
        "val_b", "val_s", "vol_b", "vol_s", "disb", "pr_vwap_b", "pr_vwap_s", "SYSTIME",
        "sec_pr_open", "sec_pr_high", "sec_pr_low", "sec_pr_close",
    ];

    public static readonly ImmutableArray<string> TradeStatsFutures =
    [
        "tradedate", "tradetime", "secid", "asset_code", "pr_open", "pr_high", "pr_low",
        "pr_close", "pr_std", "vol", "val", "trades", "pr_vwap", "pr_change", "trades_b",
        "trades_s", "val_b", "val_s", "vol_b", "vol_s", "disb", "pr_vwap_b", "pr_vwap_s",
        "im", "oi_open", "oi_high", "oi_low", "oi_close", "sec_pr_open", "sec_pr_high",
        "sec_pr_low", "sec_pr_close", "SYSTIME",
    ];

    public static readonly ImmutableArray<string> ObStatsStock =
    [
        "tradedate", "tradetime", "secid", "spread_bbo", "spread_lv10", "spread_1mio",
        "levels_b", "levels_s", "vol_b", "vol_s", "val_b", "val_s", "imbalance_vol_bbo",
        "imbalance_val_bbo", "imbalance_vol", "imbalance_val", "vwap_b", "vwap_s", "vwap_b_1mio",
        "vwap_s_1mio", "SYSTIME",
    ];

    public static readonly ImmutableArray<string> ObStatsFutures =
    [
        "tradedate", "tradetime", "secid", "asset_code", "mid_price", "micro_price", "spread_l1",
        "spread_l2", "spread_l3", "spread_l5", "spread_l10", "spread_l20", "levels_b", "levels_s",
        "vol_b_l1", "vol_b_l2", "vol_b_l3", "vol_b_l5", "vol_b_l10", "vol_b_l20", "vol_s_l1",
        "vol_s_l2", "vol_s_l3", "vol_s_l5", "vol_s_l10", "vol_s_l20", "vwap_b_l3", "vwap_b_l5",
        "vwap_b_l10", "vwap_b_l20", "vwap_s_l3", "vwap_s_l5", "vwap_s_l10", "vwap_s_l20", "SYSTIME",
    ];

    public static readonly ImmutableArray<string> OrderStatsStock =
    [
        "tradedate", "tradetime", "secid", "put_orders_b", "put_orders_s", "put_val_b", "put_val_s",
        "put_vol_b", "put_vol_s", "put_vwap_b", "put_vwap_s", "put_vol", "put_val", "put_orders",
        "cancel_orders_b", "cancel_orders_s", "cancel_val_b", "cancel_val_s", "cancel_vol_b",
        "cancel_vol_s", "cancel_vwap_b", "cancel_vwap_s", "cancel_vol", "cancel_val", "cancel_orders",
        "SYSTIME",
    ];

    public static readonly ImmutableArray<string> Hi2Stock =
    ["tradedate", "tradetime", "secid", "metric", "value", "reference", "SYSTIME"];

    public static readonly ImmutableArray<string> Hi2Futures =
    ["tradedate", "tradetime", "secid", "asset_code", "metric", "value", "reference", "SYSTIME"];

    public static readonly ImmutableArray<string> MegaAlertsStock =
    ["tradedate", "tradetime", "secid", "alert_type", "threshold", "value", "reference", "SYSTIME"];

    public static readonly ImmutableArray<string> MegaAlertsFutures =
    ["tradedate", "tradetime", "secid", "asset_code", "alert_type", "threshold", "value", "reference", "SYSTIME"];

    public static readonly ImmutableArray<string> Futoi =
    [
        "sess_id", "seqnum", "tradedate", "tradetime", "ticker", "clgroup", "pos", "pos_long",
        "pos_short", "pos_long_num", "pos_short_num", "systime", "trade_session_date",
    ];

    public static readonly ImmutableArray<string> Candles =
    ["open", "close", "high", "low", "value", "volume", "begin", "end"];

    public static readonly ImmutableArray<string> TradesStock =
    [
        "TRADENO", "TRADETIME", "SECID", "PRICE", "QUANTITY", "PERIOD", "VALUE", "BOARDID",
        "SYSTIME", "BUYSELL", "DECIMALS", "TRADINGSESSION", "TRADEDATE", "TRADE_SESSION_DATE",
    ];

    public static readonly ImmutableArray<string> TradesFutures =
    [
        "TRADENO", "BOARDNAME", "SECID", "TRADEDATE", "TRADETIME", "PRICE", "QUANTITY", "SYSTIME",
        "RECNO", "OPENPOSITION", "OFFMARKETDEAL", "BUYSELL", "TRADE_SESSION_DATE",
    ];

    public static readonly ImmutableArray<string> Orderbook =
    ["BOARDID", "SECID", "BUYSELL", "PRICE", "QUANTITY", "SEQNUM", "UPDATETIME", "DECIMALS"];

    public static readonly string TradesStockColumnsParam = string.Join(',', TradesStock);
    public static readonly string TradesFuturesColumnsParam = string.Join(',', TradesFutures);
    public static readonly string OrderbookColumnsParam = string.Join(',', Orderbook);
    public static readonly string CandlesColumnsParam = string.Join(',', Candles);
}
