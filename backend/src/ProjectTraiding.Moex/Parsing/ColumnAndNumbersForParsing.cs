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
            /// без пропусков, например dataversion. Это делает схему единым источником правды:
            /// она одновременно определяет, что запросить и что валидировать.
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
 
    }
}
