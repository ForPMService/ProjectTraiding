namespace ProjectTraiding.Moex.Infrastructure.RawCapture;

/// <summary>
/// Константы для построения ключей raw-capture.
/// Паттерн как MoexLogSources — избегаем магических строк в вызывающем коде.
/// </summary>
public static class RawCaptureClients
{
    public const string Iss = "iss";
    public const string Alg = "alg";
    public const string Calendar = "calendar";
    public const string Realtime = "realtime";
}

public static class RawCaptureErrorTypes
{
    public const string SchemaMismatch = "schema-mismatch";
    public const string ParseError = "parse-error";
    public const string HttpError = "http-error";
    public const string EmptyData = "empty-data";
    public const string BoardIdMismatch = "boardid-mismatch";
}

public static class RawCaptureDataTypes
{
    // ISS
    public const string Securities = "securities";

    // Algopack
    public const string Candles = "candles";
    public const string TradeStats = "tradestats";
    public const string OBStats = "obstats";
    public const string OrderStats = "orderstats";
    public const string Futoi = "futoi";
    public const string Hi2 = "hi2";
    public const string MegaAlerts = "alerts";

    // Calendar
    public const string OffDaysAll = "offdays-all";
    public const string OffDays = "offdays";
    public const string Sessions = "sessions";
    public const string FortsContracts = "forts-contracts";
    public const string SuspendedReasons = "suspended-reasons";
    public const string Suspended = "suspended";
    public const string SecurityAttributes = "security-attributes";
    public const string SecurityChanges = "security-changes";

    // Realtime (только MarketStatistics)
    public const string MarketStats = "marketstats";
}

public static class RawCaptureMarkets
{
    public const string Stock = "stock";
    public const string Futures = "futures";
}
