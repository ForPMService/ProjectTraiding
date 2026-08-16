namespace ProjectTraiding.Moex.Parsing
{
    public static class ColumnAndNumbersForParsing
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
            string RootKey)
        {
            /// <summary>
            /// Генерирует значение параметра *.columns для запроса к MOEX.
            /// Возвращает имена колонок через запятую в порядке Columns[].
            /// 
            /// Пример: "tradedate,tradetime,secid,pr_open,pr_high,..."
            /// 
            /// Применим к плотным схемам, где все колонки перечислены явно и SourceIndex идёт
            /// без пропусков: datashop, realtime-сделки, стакан, свечи и dataversion. Это делает
            /// схему единым источником правды: она одновременно определяет, что запросить и что
            /// валидировать.
            /// 
            /// Неприменим к схемам с пропусками в SourceIndex, например ISS securities.
            /// </summary>
            public string BuildColumnsParam()
            {
                string[] names = new string[Columns.Length];
                for (int i = 0; i < Columns.Length; i++)
                {
                    names[i] = System.Text.Encoding.UTF8.GetString(Columns[i].Name);
                }
                return string.Join(',', names);
            }
        };

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
        //
        // ВНИМАНИЕ: контракт источника изменён биржей. Проверено живым ответом 2026-07-13:
        //   /engines/stock/markets/shares/boards/tqbr/securities/SBER/trades.json
        //
        // Было 15 колонок, стало 14:
        //   — колонка TRADETIME_GRP убрана;
        //   — BOARDID переехал с позиции 2 на позицию 7;
        //   — PERIOD и VALUE поменялись местами.
        // Биржа об изменении не предупреждала.
        //
        // После включения явного списка колонок (trades.columns) порядок ниже становится
        // ЗАПРОШЕННЫМ, а не полученным: биржа возвращает колонки в том порядке, в каком мы их
        // перечислили. Схема одновременно строит запрос и проверяет ответ.
        // ═══════════════════════════════════════════════════════════

        public static readonly ExpectedSchema RealtimeTradesStockSchema = new(
            TotalColumns: 14,
            RootKey: "trades",
            Columns: new ExpectedColumn[]
            {
                new(0,  "TRADENO"u8.ToArray()),
                new(1,  "TRADETIME"u8.ToArray()),
                new(2,  "SECID"u8.ToArray()),
                new(3,  "PRICE"u8.ToArray()),
                new(4,  "QUANTITY"u8.ToArray()),
                new(5,  "PERIOD"u8.ToArray()),
                new(6,  "VALUE"u8.ToArray()),
                new(7,  "BOARDID"u8.ToArray()),
                new(8,  "SYSTIME"u8.ToArray()),
                new(9,  "BUYSELL"u8.ToArray()),
                new(10, "DECIMALS"u8.ToArray()),
                new(11, "TRADINGSESSION"u8.ToArray()),
                new(12, "TRADEDATE"u8.ToArray()),
                new(13, "TRADE_SESSION_DATE"u8.ToArray()),
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
        // rootKey: "securities", columnCount: 26, используем: 16
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
                new(9,  "SECTYPE"u8.ToArray()),        // case 8
                new(11, "ASSETCODE"u8.ToArray()),      // case 9
                new(13, "LOTVOLUME"u8.ToArray()),      // case 10
                new(14, "INITIALMARGIN"u8.ToArray()),  // case 11
                new(15, "HIGHLIMIT"u8.ToArray()),      // case 12
                new(16, "LOWLIMIT"u8.ToArray()),       // case 13
                new(17, "STEPPRICE"u8.ToArray()),      // case 14
                new(21, "BUYSELLFEE"u8.ToArray()),     // case 15
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
    }
}
