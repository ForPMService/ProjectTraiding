namespace ProjectTraiding.Moex.Parsing
{
    public class ColumnAndNumbersForParsing
    {
        /// <summary>
        /// Одна ожидаемая колонка в ответе MOEX.
        /// 
        /// SourceIndex — позиция колонки в массиве columns[] от MOEX.
        /// Name — имя колонки в виде UTF-8 байтов для сравнения через ValueTextEquals без аллокаций.
        /// </summary>
        public readonly record struct ExpectedColumn(int SourceIndex, byte[] Name);

        /// <summary>
        /// Схема одного блока данных в ответе MOEX.
        /// 
        /// TotalColumns — сколько колонок MOEX реально отдаёт в columns[].
        ///   Берётся из columns-map.json (поле columnCount).
        ///   Нужно для строгой проверки: если MOEX добавит или уберёт колонку — парсер упадёт явно.
        /// 
        /// Columns — массив колонок, которые мы используем.
        ///   Для большинства блоков совпадает с TotalColumns (берём все колонки).
        ///   Для ISS Securities — берём только нужные (9 из 27, 16 из 26), остальные пропускаем.
        /// 
        /// RootKey — имя корневого JSON-свойства, внутри которого лежат columns и data.
        ///   Например: "candles", "data", "futoi", "securities", "off_days".
        ///   Нужно для Utf8JsonReader-парсеров и для сообщений об ошибках.
        /// </summary>
        public readonly record struct ExpectedSchema(
            int TotalColumns,
            ExpectedColumn[] Columns,
            string RootKey);

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — Свечи
        // Источник: columns-map.json → "Candles (stock SBER)", "Candles (futures SiM6)"
        // rootKey: "candles", columnCount: 8, используем: все 8
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema AlgCandlesSchema = new(
            TotalColumns: 8,
            RootKey: "candles",
            Columns: new ExpectedColumn[]
            {
                new(0, "open"u8.ToArray()),
                new(1, "close"u8.ToArray()),
                new(2, "high"u8.ToArray()),
                new(3, "low"u8.ToArray()),
                new(4, "value"u8.ToArray()),
                new(5, "volume"u8.ToArray()),
                new(6, "begin"u8.ToArray()),
                new(7, "end"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — TradeStats (акции)
        // Источник: columns-map.json → "TradeStats (stock SBER)"
        // rootKey: "data", columnCount: 27, используем: все 27
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema AlgCandlesTradeStatSchema = new(
            TotalColumns: 27,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "pr_open"u8.ToArray()),
                new(4, "pr_high"u8.ToArray()),
                new(5, "pr_low"u8.ToArray()),
                new(6, "pr_close"u8.ToArray()),
                new(7, "pr_std"u8.ToArray()),
                new(8, "vol"u8.ToArray()),
                new(9, "val"u8.ToArray()),
                new(10, "trades"u8.ToArray()),
                new(11, "pr_vwap"u8.ToArray()),
                new(12, "pr_change"u8.ToArray()),
                new(13, "trades_b"u8.ToArray()),
                new(14, "trades_s"u8.ToArray()),
                new(15, "val_b"u8.ToArray()),
                new(16, "val_s"u8.ToArray()),
                new(17, "vol_b"u8.ToArray()),
                new(18, "vol_s"u8.ToArray()),
                new(19, "disb"u8.ToArray()),
                new(20, "pr_vwap_b"u8.ToArray()),
                new(21, "pr_vwap_s"u8.ToArray()),
                new(22, "SYSTIME"u8.ToArray()),
                new(23, "sec_pr_open"u8.ToArray()),
                new(24, "sec_pr_high"u8.ToArray()),
                new(25, "sec_pr_low"u8.ToArray()),
                new(26, "sec_pr_close"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — TradeStats (фьючерсы)
        // Источник: columns-map.json → "TradeStats (futures SiM6)"
        // rootKey: "data", columnCount: 33, используем: все 33
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema FuturesTradeStatsSchema = new(
            TotalColumns: 33,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "asset_code"u8.ToArray()),
                new(4, "pr_open"u8.ToArray()),
                new(5, "pr_high"u8.ToArray()),
                new(6, "pr_low"u8.ToArray()),
                new(7, "pr_close"u8.ToArray()),
                new(8, "pr_std"u8.ToArray()),
                new(9, "vol"u8.ToArray()),
                new(10, "val"u8.ToArray()),
                new(11, "trades"u8.ToArray()),
                new(12, "pr_vwap"u8.ToArray()),
                new(13, "pr_change"u8.ToArray()),
                new(14, "trades_b"u8.ToArray()),
                new(15, "trades_s"u8.ToArray()),
                new(16, "val_b"u8.ToArray()),
                new(17, "val_s"u8.ToArray()),
                new(18, "vol_b"u8.ToArray()),
                new(19, "vol_s"u8.ToArray()),
                new(20, "disb"u8.ToArray()),
                new(21, "pr_vwap_b"u8.ToArray()),
                new(22, "pr_vwap_s"u8.ToArray()),
                new(23, "im"u8.ToArray()),
                new(24, "oi_open"u8.ToArray()),
                new(25, "oi_high"u8.ToArray()),
                new(26, "oi_low"u8.ToArray()),
                new(27, "oi_close"u8.ToArray()),
                new(28, "sec_pr_open"u8.ToArray()),
                new(29, "sec_pr_high"u8.ToArray()),
                new(30, "sec_pr_low"u8.ToArray()),
                new(31, "sec_pr_close"u8.ToArray()),
                new(32, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — OBStats (акции)
        // Источник: columns-map.json → "OBStats (stock SBER)"
        // rootKey: "data", columnCount: 21, используем: все 21
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema AlgOrderBookStats5mSchema = new(
            TotalColumns: 21,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "spread_bbo"u8.ToArray()),
                new(4, "spread_lv10"u8.ToArray()),
                new(5, "spread_1mio"u8.ToArray()),
                new(6, "levels_b"u8.ToArray()),
                new(7, "levels_s"u8.ToArray()),
                new(8, "vol_b"u8.ToArray()),
                new(9, "vol_s"u8.ToArray()),
                new(10, "val_b"u8.ToArray()),
                new(11, "val_s"u8.ToArray()),
                new(12, "imbalance_vol_bbo"u8.ToArray()),
                new(13, "imbalance_val_bbo"u8.ToArray()),
                new(14, "imbalance_vol"u8.ToArray()),
                new(15, "imbalance_val"u8.ToArray()),
                new(16, "vwap_b"u8.ToArray()),
                new(17, "vwap_s"u8.ToArray()),
                new(18, "vwap_b_1mio"u8.ToArray()),
                new(19, "vwap_s_1mio"u8.ToArray()),
                new(20, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — OBStats (фьючерсы)
        // Источник: columns-map.json → "OBStats (futures SiM6)"
        // rootKey: "data", columnCount: 35, используем: все 35
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema AlgFuturesOrderBookSchema = new(
            TotalColumns: 35,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "asset_code"u8.ToArray()),
                new(4, "mid_price"u8.ToArray()),
                new(5, "micro_price"u8.ToArray()),
                new(6, "spread_l1"u8.ToArray()),
                new(7, "spread_l2"u8.ToArray()),
                new(8, "spread_l3"u8.ToArray()),
                new(9, "spread_l5"u8.ToArray()),
                new(10, "spread_l10"u8.ToArray()),
                new(11, "spread_l20"u8.ToArray()),
                new(12, "levels_b"u8.ToArray()),
                new(13, "levels_s"u8.ToArray()),
                new(14, "vol_b_l1"u8.ToArray()),
                new(15, "vol_b_l2"u8.ToArray()),
                new(16, "vol_b_l3"u8.ToArray()),
                new(17, "vol_b_l5"u8.ToArray()),
                new(18, "vol_b_l10"u8.ToArray()),
                new(19, "vol_b_l20"u8.ToArray()),
                new(20, "vol_s_l1"u8.ToArray()),
                new(21, "vol_s_l2"u8.ToArray()),
                new(22, "vol_s_l3"u8.ToArray()),
                new(23, "vol_s_l5"u8.ToArray()),
                new(24, "vol_s_l10"u8.ToArray()),
                new(25, "vol_s_l20"u8.ToArray()),
                new(26, "vwap_b_l3"u8.ToArray()),
                new(27, "vwap_b_l5"u8.ToArray()),
                new(28, "vwap_b_l10"u8.ToArray()),
                new(29, "vwap_b_l20"u8.ToArray()),
                new(30, "vwap_s_l3"u8.ToArray()),
                new(31, "vwap_s_l5"u8.ToArray()),
                new(32, "vwap_s_l10"u8.ToArray()),
                new(33, "vwap_s_l20"u8.ToArray()),
                new(34, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — OrderStats (акции)
        // Источник: columns-map.json → "OrderStats (stock SBER)"
        // rootKey: "data", columnCount: 26, используем: все 26
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema AlgOrderStats5mSchema = new(
            TotalColumns: 26,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "put_orders_b"u8.ToArray()),
                new(4, "put_orders_s"u8.ToArray()),
                new(5, "put_val_b"u8.ToArray()),
                new(6, "put_val_s"u8.ToArray()),
                new(7, "put_vol_b"u8.ToArray()),
                new(8, "put_vol_s"u8.ToArray()),
                new(9, "put_vwap_b"u8.ToArray()),
                new(10, "put_vwap_s"u8.ToArray()),
                new(11, "put_vol"u8.ToArray()),
                new(12, "put_val"u8.ToArray()),
                new(13, "put_orders"u8.ToArray()),
                new(14, "cancel_orders_b"u8.ToArray()),
                new(15, "cancel_orders_s"u8.ToArray()),
                new(16, "cancel_val_b"u8.ToArray()),
                new(17, "cancel_val_s"u8.ToArray()),
                new(18, "cancel_vol_b"u8.ToArray()),
                new(19, "cancel_vol_s"u8.ToArray()),
                new(20, "cancel_vwap_b"u8.ToArray()),
                new(21, "cancel_vwap_s"u8.ToArray()),
                new(22, "cancel_vol"u8.ToArray()),
                new(23, "cancel_val"u8.ToArray()),
                new(24, "cancel_orders"u8.ToArray()),
                new(25, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — Cursor пагинации
        // Используется всеми ALGOPACK-парсерами с cursor-пагинацией
        // rootKey: "data.cursor", columnCount: 3, используем: все 3
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema AlgCandlesDataCursorSchema = new(
            TotalColumns: 3,
            RootKey: "data.cursor",
            Columns: new ExpectedColumn[]
            {
                new(0, "INDEX"u8.ToArray()),
                new(1, "TOTAL"u8.ToArray()),
                new(2, "PAGESIZE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — Hi2 (акции)
        // Источник: columns-map.json → "Hi2 (stock SBER)"
        // rootKey: "data", columnCount: 7, используем: все 7
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema Hi2AssetSchema = new(
            TotalColumns: 7,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "metric"u8.ToArray()),
                new(4, "value"u8.ToArray()),
                new(5, "reference"u8.ToArray()),
                new(6, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — Hi2 (фьючерсы)
        // Источник: columns-map.json → "Hi2 (futures SiM6)"
        // rootKey: "data", columnCount: 8, используем: все 8
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema Hi2FuturesSchema = new(
            TotalColumns: 8,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "asset_code"u8.ToArray()),
                new(4, "metric"u8.ToArray()),
                new(5, "value"u8.ToArray()),
                new(6, "reference"u8.ToArray()),
                new(7, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — MegaAlerts (акции)
        // Источник: columns-map.json → "MegaAlerts (stock SBER)"
        // rootKey: "data", columnCount: 8, используем: все 8
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema MegaAlertsAssetSchema = new(
            TotalColumns: 8,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "alert_type"u8.ToArray()),
                new(4, "threshold"u8.ToArray()),
                new(5, "value"u8.ToArray()),
                new(6, "reference"u8.ToArray()),
                new(7, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — MegaAlerts (фьючерсы)
        // Источник: columns-map.json → "MegaAlerts (futures SiM6)"
        // rootKey: "data", columnCount: 9, используем: все 9
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema MegaAlertsFuturesSchema = new(
            TotalColumns: 9,
            RootKey: "data",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradetime"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "asset_code"u8.ToArray()),
                new(4, "alert_type"u8.ToArray()),
                new(5, "threshold"u8.ToArray()),
                new(6, "value"u8.ToArray()),
                new(7, "reference"u8.ToArray()),
                new(8, "SYSTIME"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ALGOPACK — FUTOI
        // Источник: columns-map.json → "Futoi (Si)"
        // rootKey: "futoi", columnCount: 13, используем: все 13
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema FutoiSchema = new(
            TotalColumns: 13,
            RootKey: "futoi",
            Columns: new ExpectedColumn[]
            {
                new(0, "sess_id"u8.ToArray()),
                new(1, "seqnum"u8.ToArray()),
                new(2, "tradedate"u8.ToArray()),
                new(3, "tradetime"u8.ToArray()),
                new(4, "ticker"u8.ToArray()),
                new(5, "clgroup"u8.ToArray()),
                new(6, "pos"u8.ToArray()),
                new(7, "pos_long"u8.ToArray()),
                new(8, "pos_short"u8.ToArray()),
                new(9, "pos_long_num"u8.ToArray()),
                new(10, "pos_short_num"u8.ToArray()),
                new(11, "systime"u8.ToArray()),
                new(12, "trade_session_date"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ISS — Акции (фондовый рынок)
        // Источник: columns-map.json → "Securities (stock TQBR)"
        // rootKey: "securities", columnCount: 27, используем: 9 из 27
        // 
        // Внимание: SourceIndex идёт с пропусками (0,1,2,4,5,9,11,17,22).
        // TotalColumns нужен для валидации общего количества колонок,
        // а Columns содержит только те, которые мы парсим.
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema IssStockSecuritySchema = new(
            TotalColumns: 27,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0, "SECID"u8.ToArray()),
                new(1, "BOARDID"u8.ToArray()),
                new(2, "SHORTNAME"u8.ToArray()),
                new(4, "LOTSIZE"u8.ToArray()),
                new(5, "FACEVALUE"u8.ToArray()),
                new(9, "SECNAME"u8.ToArray()),
                new(11, "MARKETCODE"u8.ToArray()),
                new(17, "PREVDATE"u8.ToArray()),
                new(22, "PREVLEGALCLOSEPRICE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // ISS — Фьючерсы (срочный рынок)
        // Источник: columns-map.json → "Securities (futures RFUD)"
        // rootKey: "securities", columnCount: 26, используем: 16 из 26
        // 
        // Внимание: SourceIndex идёт с пропусками (0,2,3,4,5,6,7,8,11,12,13,14,15,16,17,19).
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema IssFuturesSecuritySchema = new(
            TotalColumns: 26,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0, "SECID"u8.ToArray()),
                new(2, "SHORTNAME"u8.ToArray()),
                new(3, "SECNAME"u8.ToArray()),
                new(4, "PREVSETTLEPRICE"u8.ToArray()),
                new(5, "DECIMALS"u8.ToArray()),
                new(6, "MINSTEP"u8.ToArray()),
                new(7, "LASTTRADEDATE"u8.ToArray()),
                new(8, "LASTDELDATE"u8.ToArray()),
                new(11, "ASSETCODE"u8.ToArray()),
                new(12, "PREVOPENPOSITION"u8.ToArray()),
                new(13, "LOTVOLUME"u8.ToArray()),
                new(14, "INITIALMARGIN"u8.ToArray()),
                new(15, "HIGHLIMIT"u8.ToArray()),
                new(16, "LOWLIMIT"u8.ToArray()),
                new(17, "STEPPRICE"u8.ToArray()),
                new(19, "PREVPRICE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Выходные дни (все рынки)
        // Источник: columns-map.json → "Calendar OffDays All"
        // rootKey: "off_days", columnCount: 10, используем: все 10
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarOffDaysAllSchema = new(
            TotalColumns: 10,
            RootKey: "off_days",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "currency_workday"u8.ToArray()),
                new(2, "currency_trade_session_date"u8.ToArray()),
                new(3, "currency_reason"u8.ToArray()),
                new(4, "futures_workday"u8.ToArray()),
                new(5, "futures_trade_session_date"u8.ToArray()),
                new(6, "futures_reason"u8.ToArray()),
                new(7, "stock_workday"u8.ToArray()),
                new(8, "stock_trade_session_date"u8.ToArray()),
                new(9, "stock_reason"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Выходные дни (один рынок: stock или futures)
        // Источник: columns-map.json → "Calendar Stock OffDays", "Calendar Futures OffDays"
        // rootKey: "off_days", columnCount: 5, используем: все 5
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarOffDaysMarketSchema = new(
            TotalColumns: 5,
            RootKey: "off_days",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "is_traded"u8.ToArray()),
                new(2, "trade_session_date"u8.ToArray()),
                new(3, "reason"u8.ToArray()),
                new(4, "updatetime"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Сессии фондового рынка
        // Источник: columns-map.json → "Calendar Stock Session → session_schedule"
        // rootKey: "session_schedule", columnCount: 8, используем: все 8
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarStockSessionSchema = new(
            TotalColumns: 8,
            RootKey: "session_schedule",
            Columns: new ExpectedColumn[]
            {
                new(0, "tradedate"u8.ToArray()),
                new(1, "tradingsession"u8.ToArray()),
                new(2, "boardid"u8.ToArray()),
                new(3, "secid"u8.ToArray()),
                new(4, "type"u8.ToArray()),
                new(5, "time_from"u8.ToArray()),
                new(6, "time_till"u8.ToArray()),
                new(7, "updatetime"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Сессии срочного рынка
        // Источник: columns-map.json → "Calendar Futures Session → session_schedule"
        // rootKey: "session_schedule", columnCount: 7, используем: все 7
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarFuturesSessionSchema = new(
            TotalColumns: 7,
            RootKey: "session_schedule",
            Columns: new ExpectedColumn[]
            {
                new(0, "trade_session_date"u8.ToArray()),
                new(1, "boardid"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "type"u8.ToArray()),
                new(4, "time_from"u8.ToArray()),
                new(5, "time_till"u8.ToArray()),
                new(6, "updatetime"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Типы сессий (общий для stock и futures)
        // Источник: columns-map.json → "session_schedule.types"
        // rootKey: "session_schedule.types", columnCount: 2, используем: все 2
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarSessionTypesSchema = new(
            TotalColumns: 2,
            RootKey: "session_schedule.types",
            Columns: new ExpectedColumn[]
            {
                new(0, "type"u8.ToArray()),
                new(1, "title"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Фьючерсные контракты FORTS
        // Источник: columns-map.json → "Calendar Futures Securities → forts"
        // rootKey: "forts", columnCount: 10, используем: все 10
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarFortsContractsSchema = new(
            TotalColumns: 10,
            RootKey: "forts",
            Columns: new ExpectedColumn[]
            {
                new(0, "secid"u8.ToArray()),
                new(1, "asset_code"u8.ToArray()),
                new(2, "shortname"u8.ToArray()),
                new(3, "exec_type"u8.ToArray()),
                new(4, "contract_name"u8.ToArray()),
                new(5, "expiration_date"u8.ToArray()),
                new(6, "end_date"u8.ToArray()),
                new(7, "expiration_type"u8.ToArray()),
                new(8, "expiration_time"u8.ToArray()),
                new(9, "weekend_session"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Опционные серии
        // Источник: columns-map.json → "Calendar Futures Securities → options"
        // rootKey: "options", columnCount: 11, используем: все 11
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarOptionsSeriesSchema = new(
            TotalColumns: 11,
            RootKey: "options",
            Columns: new ExpectedColumn[]
            {
                new(0, "asset_type_name"u8.ToArray()),
                new(1, "asset_code"u8.ToArray()),
                new(2, "series_name"u8.ToArray()),
                new(3, "series_type"u8.ToArray()),
                new(4, "exec_type"u8.ToArray()),
                new(5, "margin_style"u8.ToArray()),
                new(6, "contract_name"u8.ToArray()),
                new(7, "expiration_date"u8.ToArray()),
                new(8, "expiration_type"u8.ToArray()),
                new(9, "expiration_time"u8.ToArray()),
                new(10, "weekend_session"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Приостановки торгов
        // Источник: columns-map.json → "Calendar Suspended → suspended"
        // rootKey: "suspended", columnCount: 8, используем: все 8
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarSuspendedSchema = new(
            TotalColumns: 8,
            RootKey: "suspended",
            Columns: new ExpectedColumn[]
            {
                new(0, "secid"u8.ToArray()),
                new(1, "reason_id"u8.ToArray()),
                new(2, "date_from"u8.ToArray()),
                new(3, "date_till"u8.ToArray()),
                new(4, "boardid"u8.ToArray()),
                new(5, "settle_codes"u8.ToArray()),
                new(6, "changedate"u8.ToArray()),
                new(7, "updatetime"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Причины приостановок
        // Источник: columns-map.json → "Calendar Suspended → suspended.reasons"
        // rootKey: "suspended.reasons", columnCount: 2, используем: все 2
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarSuspendedReasonsSchema = new(
            TotalColumns: 2,
            RootKey: "suspended.reasons",
            Columns: new ExpectedColumn[]
            {
                new(0, "id"u8.ToArray()),
                new(1, "title"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Изменения параметров инструментов
        // Источник: columns-map.json → "Calendar Security Changes → securities"
        // rootKey: "securities", columnCount: 6, используем: все 6
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarSecurityChangesSchema = new(
            TotalColumns: 6,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0, "updatetime"u8.ToArray()),
                new(1, "action"u8.ToArray()),
                new(2, "secid"u8.ToArray()),
                new(3, "attribute_name"u8.ToArray()),
                new(4, "before_value"u8.ToArray()),
                new(5, "after_value"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Справочник атрибутов инструментов
        // Источник: columns-map.json → "Calendar Security Changes → securities.attributes"
        // rootKey: "securities.attributes", columnCount: 3, используем: все 3
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarSecurityAttributesSchema = new(
            TotalColumns: 3,
            RootKey: "securities.attributes",
            Columns: new ExpectedColumn[]
            {
                new(0, "name"u8.ToArray()),
                new(1, "type"u8.ToArray()),
                new(2, "title"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Календарь — Cursor пагинации
        // Источник: columns-map.json → "suspended.cursor", "securities.cursor"
        // rootKey: передаётся динамически ("suspended.cursor" или "securities.cursor")
        // columnCount: 3, используем: все 3
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema CalendarCursorSchema = new(
            TotalColumns: 3,
            RootKey: "cursor",
            Columns: new ExpectedColumn[]
            {
                new(0, "INDEX"u8.ToArray()),
                new(1, "TOTAL"u8.ToArray()),
                new(2, "PAGESIZE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Real-time — Orderbook (стакан котировок)
        // Источник: raw fixtures orderbook-stock-raw.json, orderbook-futures-raw.json
        // rootKey: "orderbook", columnCount: 8, используем: все 8
        //
        // Структура stock и futures идентична.
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema RealtimeOrderbookSchema = new(
            TotalColumns: 8,
            RootKey: "orderbook",
            Columns: new ExpectedColumn[]
            {
                new(0, "BOARDID"u8.ToArray()),
                new(1, "SECID"u8.ToArray()),
                new(2, "BUYSELL"u8.ToArray()),
                new(3, "PRICE"u8.ToArray()),
                new(4, "QUANTITY"u8.ToArray()),
                new(5, "SEQNUM"u8.ToArray()),
                new(6, "UPDATETIME"u8.ToArray()),
                new(7, "DECIMALS"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Real-time — DataVersion (служебный блок версии данных)
        // Источник: raw fixtures orderbook-*-raw.json, trades-*-raw.json
        // rootKey: "dataversion", columnCount: 4, используем: все 4
        //
        // Вложен в ответы orderbook и trades.
        // Структура stock и futures идентична.
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema RealtimeDataVersionSchema = new(
            TotalColumns: 4,
            RootKey: "dataversion",
            Columns: new ExpectedColumn[]
            {
                new(0, "data_version"u8.ToArray()),
                new(1, "seqnum"u8.ToArray()),
                new(2, "trade_date"u8.ToArray()),
                new(3, "trade_session_date"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Real-time — Trades Stock (сделки по акциям)
        // Источник: raw fixture trades-stock-raw.json
        // rootKey: "trades", columnCount: 15, используем: все 15
        //
        // Внимание: rootKey совпадает с futures ("trades"),
        // но набор и количество колонок разные.
        // Парсер выбирает схему по контексту вызова (stock vs futures endpoint).
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema RealtimeTradesStockSchema = new(
            TotalColumns: 15,
            RootKey: "trades",
            Columns: new ExpectedColumn[]
            {
                new(0,  "TRADENO"u8.ToArray()),
                new(1,  "TRADETIME"u8.ToArray()),
                new(2,  "BOARDID"u8.ToArray()),
                new(3,  "SECID"u8.ToArray()),
                new(4,  "PRICE"u8.ToArray()),
                new(5,  "QUANTITY"u8.ToArray()),
                new(6,  "VALUE"u8.ToArray()),
                new(7,  "PERIOD"u8.ToArray()),
                new(8,  "TRADETIME_GRP"u8.ToArray()),
                new(9,  "SYSTIME"u8.ToArray()),
                new(10, "BUYSELL"u8.ToArray()),
                new(11, "DECIMALS"u8.ToArray()),
                new(12, "TRADINGSESSION"u8.ToArray()),
                new(13, "TRADEDATE"u8.ToArray()),
                new(14, "TRADE_SESSION_DATE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Real-time — Trades Futures (сделки по фьючерсам)
        // Источник: raw fixture trades-futures-raw.json
        // rootKey: "trades", columnCount: 13, используем: все 13
        //
        // Внимание: rootKey совпадает со stock ("trades"),
        // но набор и количество колонок разные.
        // Парсер выбирает схему по контексту вызова (stock vs futures endpoint).
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema RealtimeTradesFuturesSchema = new(
            TotalColumns: 13,
            RootKey: "trades",
            Columns: new ExpectedColumn[]
            {
                new(0,  "TRADENO"u8.ToArray()),
                new(1,  "BOARDNAME"u8.ToArray()),
                new(2,  "SECID"u8.ToArray()),
                new(3,  "TRADEDATE"u8.ToArray()),
                new(4,  "TRADETIME"u8.ToArray()),
                new(5,  "PRICE"u8.ToArray()),
                new(6,  "QUANTITY"u8.ToArray()),
                new(7,  "SYSTIME"u8.ToArray()),
                new(8,  "RECNO"u8.ToArray()),
                new(9,  "OPENPOSITION"u8.ToArray()),
                new(10, "OFFMARKETDEAL"u8.ToArray()),
                new(11, "BUYSELL"u8.ToArray()),
                new(12, "TRADE_SESSION_DATE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // Real-time — Trades Yields (блок доходности сделок)
        // Источник: raw fixtures trades-stock-raw.json, trades-futures-raw.json
        // rootKey: "trades_yields", columnCount: 2, используем: все 2
        //
        // В текущих raw samples блок приходит с columns, но без data.
        // Схема нужна, чтобы parser фиксировал наличие блока
        // и не падал при валидации.
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema RealtimeTradesYieldsSchema = new(
            TotalColumns: 2,
            RootKey: "trades_yields",
            Columns: new ExpectedColumn[]
            {
                new(0, "boardid"u8.ToArray()),
                new(1, "secid"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // MarketStatistics — Securities (акции, TQBR)
        // Источник: /engines/stock/.../securities/{ticker}.json?iss.only=securities
        // rootKey: "securities", columnCount: 27, используем: 10 из 27
        // SourceIndex с пропусками: 0,1,6,8,14,18,19,23,25,26
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema MarketStatisticsStockSecuritiesSchema = new(
            TotalColumns: 27,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0,  "SECID"u8.ToArray()),
                new(1,  "BOARDID"u8.ToArray()),
                new(6,  "STATUS"u8.ToArray()),
                new(8,  "DECIMALS"u8.ToArray()),
                new(14, "MINSTEP"u8.ToArray()),
                new(18, "ISSUESIZE"u8.ToArray()),
                new(19, "ISIN"u8.ToArray()),
                new(23, "CURRENCYID"u8.ToArray()),
                new(25, "LISTLEVEL"u8.ToArray()),
                new(26, "SETTLEDATE"u8.ToArray()),
            });

        // ═══════════════════════════════════════════════════════════
        // MarketStatistics — Securities (фьючерсы, RFUD)
        // Источник: /engines/futures/.../securities/{ticker}.json?iss.only=securities
        // rootKey: "securities", columnCount: 26, используем: 7 из 26
        // SourceIndex с пропусками: 0,1,18,20,21,22,25
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema MarketStatisticsFuturesSecuritiesSchema = new(
            TotalColumns: 26,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0,  "SECID"u8.ToArray()),
                new(1,  "BOARDID"u8.ToArray()),
                new(18, "LASTSETTLEPRICE"u8.ToArray()),
                new(20, "IMTIME"u8.ToArray()),
                new(21, "BUYSELLFEE"u8.ToArray()),
                new(22, "SCALPERFEE"u8.ToArray()),
                new(25, "SETTLEPRICE_CLR"u8.ToArray()),
            });
            
        
        // ═══════════════════════════════════════════════════════════
        // Карточка акции — securities
        // Источник: /engines/stock/markets/shares/boards/tqbr/securities.json
        // Probe: probe-13, 2026-06-01
        // rootKey: "securities", columnCount: 27, используем: 13
        // ═══════════════════════════════════════════════════════════
 
        public static readonly ExpectedSchema StockCardSecuritiesSchema = new(
            TotalColumns: 27,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0,  "SECID"u8.ToArray()),        // case 0
                new(1,  "BOARDID"u8.ToArray()),      // case 1
                new(2,  "SHORTNAME"u8.ToArray()),    // case 2
                new(4,  "LOTSIZE"u8.ToArray()),      // case 3
                new(6,  "STATUS"u8.ToArray()),       // case 4
                new(8,  "DECIMALS"u8.ToArray()),     // case 5
                new(9,  "SECNAME"u8.ToArray()),      // case 6
                new(14, "MINSTEP"u8.ToArray()),      // case 7
                new(18, "ISSUESIZE"u8.ToArray()),    // case 8
                new(19, "ISIN"u8.ToArray()),         // case 9
                new(23, "CURRENCYID"u8.ToArray()),   // case 10
                new(24, "SECTYPE"u8.ToArray()),      // case 11
                new(25, "LISTLEVEL"u8.ToArray()),    // case 12
            });
 
        // ═══════════════════════════════════════════════════════════
        // Карточка акции — marketdata
        // Источник: тот же ответ, блок "marketdata"
        // Probe: probe-13, 2026-06-01
        // rootKey: "marketdata", columnCount: 56, используем: 12
        // ═══════════════════════════════════════════════════════════
 
        public static readonly ExpectedSchema StockCardMarketdataSchema = new(
            TotalColumns: 56,
            RootKey: "marketdata",
            Columns: new ExpectedColumn[]
            {
                new(2,  "BID"u8.ToArray()),            // case 0
                new(4,  "OFFER"u8.ToArray()),          // case 1
                new(6,  "SPREAD"u8.ToArray()),         // case 2
                new(9,  "OPEN"u8.ToArray()),           // case 3
                new(10, "LOW"u8.ToArray()),            // case 4
                new(11, "HIGH"u8.ToArray()),           // case 5
                new(12, "LAST"u8.ToArray()),           // case 6
                new(26, "NUMTRADES"u8.ToArray()),      // case 7
                new(27, "VOLTODAY"u8.ToArray()),       // case 8
                new(28, "VALTODAY"u8.ToArray()),       // case 9
                new(31, "TRADINGSTATUS"u8.ToArray()),  // case 10
                new(32, "UPDATETIME"u8.ToArray()),     // case 11
            });
 
        // ═══════════════════════════════════════════════════════════
        // Карточка фьючерса — securities
        // Источник: /engines/futures/markets/forts/boards/RFUD/securities.json
        // Probe: probe-14, 2026-06-01
        // rootKey: "securities", columnCount: 26, используем: 15
        // ═══════════════════════════════════════════════════════════
 
        public static readonly ExpectedSchema FuturesCardSecuritiesSchema = new(
            TotalColumns: 26,
            RootKey: "securities",
            Columns: new ExpectedColumn[]
            {
                new(0,  "SECID"u8.ToArray()),          // case 0
                new(1,  "BOARDID"u8.ToArray()),        // case 1
                new(2,  "SHORTNAME"u8.ToArray()),      // case 2
                new(3,  "SECNAME"u8.ToArray()),        // case 3
                new(5,  "DECIMALS"u8.ToArray()),       // case 4
                new(6,  "MINSTEP"u8.ToArray()),        // case 5
                new(7,  "LASTTRADEDATE"u8.ToArray()),  // case 6
                new(8,  "LASTDELDATE"u8.ToArray()),    // case 7
                new(11, "ASSETCODE"u8.ToArray()),      // case 8
                new(13, "LOTVOLUME"u8.ToArray()),      // case 9
                new(14, "INITIALMARGIN"u8.ToArray()),  // case 10
                new(15, "HIGHLIMIT"u8.ToArray()),      // case 11
                new(16, "LOWLIMIT"u8.ToArray()),       // case 12
                new(17, "STEPPRICE"u8.ToArray()),      // case 13
                new(21, "BUYSELLFEE"u8.ToArray()),     // case 14
            });
 
        // ═══════════════════════════════════════════════════════════
        // Карточка фьючерса — marketdata
        // Источник: тот же ответ, блок "marketdata"
        // Probe: probe-14, 2026-06-01
        // rootKey: "marketdata", columnCount: 37, используем: 14
        // ═══════════════════════════════════════════════════════════
 
        public static readonly ExpectedSchema FuturesCardMarketdataSchema = new(
            TotalColumns: 37,
            RootKey: "marketdata",
            Columns: new ExpectedColumn[]
            {
                new(2,  "BID"u8.ToArray()),            // case 0
                new(3,  "OFFER"u8.ToArray()),          // case 1
                new(4,  "SPREAD"u8.ToArray()),         // case 2
                new(5,  "OPEN"u8.ToArray()),           // case 3
                new(6,  "HIGH"u8.ToArray()),           // case 4
                new(7,  "LOW"u8.ToArray()),            // case 5
                new(8,  "LAST"u8.ToArray()),           // case 6
                new(11, "SETTLEPRICE"u8.ToArray()),    // case 7
                new(13, "OPENPOSITION"u8.ToArray()),   // case 8
                new(14, "NUMTRADES"u8.ToArray()),      // case 9
                new(15, "VOLTODAY"u8.ToArray()),       // case 10
                new(16, "VALTODAY"u8.ToArray()),       // case 11
                new(18, "UPDATETIME"u8.ToArray()),     // case 12
                new(32, "OICHANGE"u8.ToArray()),       // case 13
            });
        // ═══════════════════════════════════════════════════════════
        // Обратная совместимость
        // ═══════════════════════════════════════════════════════════
        // HISTORICAL: backward-compat aliases for old JsonDocument parsers.
        // All callers migrated to Utf8 parsers. Kept for audit, uncomment if needed.
        /*

        public static ExpectedColumn[] AlgCandlesExpectedColumns => AlgCandlesSchema.Columns;
        public static ExpectedColumn[] AlgCandlesTradeStatExpectedColumns => AlgCandlesTradeStatSchema.Columns;
        public static ExpectedColumn[] FuturesTradeStatsExpectedColumns => FuturesTradeStatsSchema.Columns;
        public static ExpectedColumn[] AlgOrderBookStats5mExpectedColumns => AlgOrderBookStats5mSchema.Columns;
        public static ExpectedColumn[] AlgFuturesOrderBookExpectedColumns => AlgFuturesOrderBookSchema.Columns;
        public static ExpectedColumn[] AlgOrderStats5mExpectedColumns => AlgOrderStats5mSchema.Columns;
        public static ExpectedColumn[] AlgCandlesDataCursorExpectedColumns => AlgCandlesDataCursorSchema.Columns;
        public static ExpectedColumn[] Hi2AssetExpectedColumns => Hi2AssetSchema.Columns;
        public static ExpectedColumn[] Hi2FuturesExpectedColumns => Hi2FuturesSchema.Columns;
        public static ExpectedColumn[] MegaAlertsAssetExpectedColumns => MegaAlertsAssetSchema.Columns;
        public static ExpectedColumn[] MegaAlertsFuturesExpectedColumns => MegaAlertsFuturesSchema.Columns;
        public static ExpectedColumn[] FutoiExpectedColumns => FutoiSchema.Columns;
        public static ExpectedColumn[] IssStockSecurityExpectedColumns => IssStockSecuritySchema.Columns;
        public static ExpectedColumn[] IssFuturesSecurityExpectedColumns => IssFuturesSecuritySchema.Columns;
        public static ExpectedColumn[] CalendarOffDaysAllExpectedColumns => CalendarOffDaysAllSchema.Columns;
        public static ExpectedColumn[] CalendarOffDaysMarketExpectedColumns => CalendarOffDaysMarketSchema.Columns;
        public static ExpectedColumn[] CalendarStockSessionExpectedColumns => CalendarStockSessionSchema.Columns;
        public static ExpectedColumn[] CalendarFuturesSessionExpectedColumns => CalendarFuturesSessionSchema.Columns;
        public static ExpectedColumn[] CalendarSessionTypesExpectedColumns => CalendarSessionTypesSchema.Columns;
        public static ExpectedColumn[] CalendarFortsContractsExpectedColumns => CalendarFortsContractsSchema.Columns;
        public static ExpectedColumn[] CalendarOptionsSeriesExpectedColumns => CalendarOptionsSeriesSchema.Columns;
        public static ExpectedColumn[] CalendarSuspendedExpectedColumns => CalendarSuspendedSchema.Columns;
        public static ExpectedColumn[] CalendarSuspendedReasonsExpectedColumns => CalendarSuspendedReasonsSchema.Columns;
        public static ExpectedColumn[] CalendarSecurityChangesExpectedColumns => CalendarSecurityChangesSchema.Columns;
        public static ExpectedColumn[] CalendarSecurityAttributesExpectedColumns => CalendarSecurityAttributesSchema.Columns;
        public static ExpectedColumn[] CalendarCursorExpectedColumns => CalendarCursorSchema.Columns;
        */
    }
}
