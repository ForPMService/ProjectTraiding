namespace ProjectTraiding.Moex.Contracts
{
    /// <summary>
    /// Предметные правила загрузки Московской биржи. Это не
    /// Infrastructure.Telemetry.MoexDataKinds: тот класс задаёт только метки
    /// телеметрии и не является словарём допустимых видов задач.
    /// </summary>
    public static class MoexDomainRules
    {
        private static readonly string[] MarketValues = ["stock", "futures"];
        private static readonly string[] DataKindValues =
            ["candles", "tradestats", "obstats", "orderstats", "futoi", "hi2", "mega_alerts"];
        private static readonly int[] CandleIntervalValues = [1, 10, 60, 24];

        public static IReadOnlyList<string> Markets => MarketValues;
        public static IReadOnlyList<string> DataKinds => DataKindValues;
        public static IReadOnlyList<int> CandleIntervals => CandleIntervalValues;

        public static bool IsMarket(string? market) => market is "stock" or "futures";

        public static bool IsDataKind(string? dataKind) =>
            dataKind is "candles" or "tradestats" or "obstats" or "orderstats"
                or "futoi" or "hi2" or "mega_alerts";

        public static bool IsMarketAllowedForDataKind(string? market, string? dataKind) =>
            (dataKind, market) switch
            {
                ("orderstats", "stock") => true,
                ("futoi", "futures") => true,
                ("orderstats" or "futoi", _) => false,
                (_, "stock" or "futures") => IsDataKind(dataKind),
                _ => false
            };

        public static IReadOnlyList<string> GetAllowedMarketsForDataKind(string dataKind) =>
            dataKind is "orderstats" ? ["stock"]
            : dataKind is "futoi" ? ["futures"]
            : MarketValues;

        public static bool RequiresCandleInterval(string? dataKind) => dataKind == "candles";

        public static bool IsCandleInterval(int? candleInterval) =>
            candleInterval is 1 or 10 or 60 or 24;
    }
}
