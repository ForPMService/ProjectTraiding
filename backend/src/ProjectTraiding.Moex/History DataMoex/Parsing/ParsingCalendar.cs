// LEGACY: not used by Phase 8 mappers. Production path is ParsingCalendarUtf8.
// Removal: separate cleanup task after Phase 8-D. Lock §11.
using History_DataMoex.Contracts.Dto;
using History_DataMoex.Contracts.Dto.Calendar;
using System.Text.Json;

namespace History_DataMoex.Parsing
{
    public class ParsingCalendar
    {
        // HISTORICAL: kept for source-contract audit. All callers migrated to Utf8 parsers (B9.5 / B10).
        // Uncomment only if need to compare JsonDocument vs Utf8JsonReader output.
        /*
        // ── Off Days (общий, все рынки) ─────────────────────────

        public static List<CalendarOffDaysAllDTO> ParseCalendarOffDaysAll(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "off_days");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarOffDaysAllDTO> result = new List<CalendarOffDaysAllDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarOffDaysAllDTO
                {
                    TradeDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[0].SourceIndex]),
                    CurrencyWorkday = ParseHelpers.GetLongOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[1].SourceIndex]),
                    CurrencyTradeSessionDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[2].SourceIndex]),
                    CurrencyReason = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[3].SourceIndex]),
                    FuturesWorkday = ParseHelpers.GetLongOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[4].SourceIndex]),
                    FuturesTradeSessionDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[5].SourceIndex]),
                    FuturesReason = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[6].SourceIndex]),
                    StockWorkday = ParseHelpers.GetLongOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[7].SourceIndex]),
                    StockTradeSessionDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[8].SourceIndex]),
                    StockReason = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysAllExpectedColumns[9].SourceIndex])
                });
            }

            return result;
        }

        // ── Off Days (один рынок: stock или futures) ────────────

        public static List<CalendarOffDaysMarketDTO> ParseCalendarOffDaysMarket(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "off_days");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarOffDaysMarketExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarOffDaysMarketDTO> result = new List<CalendarOffDaysMarketDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarOffDaysMarketDTO
                {
                    TradeDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysMarketExpectedColumns[0].SourceIndex]),
                    IsTraded = ParseHelpers.GetIntOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysMarketExpectedColumns[1].SourceIndex]),
                    TradeSessionDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysMarketExpectedColumns[2].SourceIndex]),
                    Reason = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysMarketExpectedColumns[3].SourceIndex]),
                    UpdateTime = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarOffDaysMarketExpectedColumns[4].SourceIndex])
                });
            }

            return result;
        }

        // ── Stock Session ───────────────────────────────────────

        public static List<CalendarStockSessionDTO> ParseCalendarStockSession(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "session_schedule");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarStockSessionDTO> result = new List<CalendarStockSessionDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarStockSessionDTO
                {
                    TradeDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[0].SourceIndex]),
                    TradingSession = ParseHelpers.GetIntOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[1].SourceIndex]),
                    BoardId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[2].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[3].SourceIndex]),
                    Type = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[4].SourceIndex]),
                    TimeFrom = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[5].SourceIndex]),
                    TimeTill = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[6].SourceIndex]),
                    UpdateTime = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarStockSessionExpectedColumns[7].SourceIndex])
                });
            }

            return result;
        }

        // ── Futures Session ─────────────────────────────────────

        public static List<CalendarFuturesSessionDTO> ParseCalendarFuturesSession(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "session_schedule");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarFuturesSessionDTO> result = new List<CalendarFuturesSessionDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarFuturesSessionDTO
                {
                    TradeSessionDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[0].SourceIndex]),
                    BoardId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[2].SourceIndex]),
                    Type = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[3].SourceIndex]),
                    TimeFrom = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[4].SourceIndex]),
                    TimeTill = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[5].SourceIndex]),
                    UpdateTime = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarFuturesSessionExpectedColumns[6].SourceIndex])
                });
            }

            return result;
        }

        // ── Session Types (общий для stock и futures) ───────────

        public static List<CalendarSessionTypeDTO> ParseCalendarSessionTypes(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "session_schedule.types");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarSessionTypesExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarSessionTypeDTO> result = new List<CalendarSessionTypeDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarSessionTypeDTO
                {
                    Type = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSessionTypesExpectedColumns[0].SourceIndex]),
                    Title = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSessionTypesExpectedColumns[1].SourceIndex])
                });
            }

            return result;
        }

        // ── Forts Contracts ─────────────────────────────────────

        public static List<CalendarFortsContractDTO> ParseCalendarFortsContracts(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "forts");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarFortsContractDTO> result = new List<CalendarFortsContractDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarFortsContractDTO
                {
                    SecId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[0].SourceIndex]),
                    AssetCode = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[1].SourceIndex]),
                    ShortName = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[2].SourceIndex]),
                    ExecType = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[3].SourceIndex]),
                    ContractName = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[4].SourceIndex]),
                    ExpirationDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[5].SourceIndex]),
                    EndDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[6].SourceIndex]),
                    ExpirationType = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[7].SourceIndex]),
                    ExpirationTime = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[8].SourceIndex]),
                    WeekendSession = ParseHelpers.GetIntOrNull(data[i][ColumnAndNumbersForParsing.CalendarFortsContractsExpectedColumns[9].SourceIndex])
                });
            }

            return result;
        }

        // ── Options Series ──────────────────────────────────────

        public static List<CalendarOptionsSeriesDTO> ParseCalendarOptionsSeries(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "options");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarOptionsSeriesDTO> result = new List<CalendarOptionsSeriesDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarOptionsSeriesDTO
                {
                    AssetTypeName = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[0].SourceIndex]),
                    AssetCode = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[1].SourceIndex]),
                    SeriesName = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[2].SourceIndex]),
                    SeriesType = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[3].SourceIndex]),
                    ExecType = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[4].SourceIndex]),
                    MarginStyle = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[5].SourceIndex]),
                    ContractName = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[6].SourceIndex]),
                    ExpirationDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[7].SourceIndex]),
                    ExpirationType = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[8].SourceIndex]),
                    ExpirationTime = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[9].SourceIndex]),
                    WeekendSession = ParseHelpers.GetIntOrNull(data[i][ColumnAndNumbersForParsing.CalendarOptionsSeriesExpectedColumns[10].SourceIndex])
                });
            }

            return result;
        }

        // ── Suspended ───────────────────────────────────────────

        public static List<CalendarSuspendedDTO> ParseCalendarSuspended(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "suspended");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarSuspendedDTO> result = new List<CalendarSuspendedDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarSuspendedDTO
                {
                    SecId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[0].SourceIndex]),
                    ReasonId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[1].SourceIndex]),
                    DateFrom = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[2].SourceIndex]),
                    DateTill = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[3].SourceIndex]),
                    BoardId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[4].SourceIndex]),
                    SettleCodes = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[5].SourceIndex]),
                    ChangeDate = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[6].SourceIndex]),
                    UpdateTime = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedExpectedColumns[7].SourceIndex])
                });
            }

            return result;
        }

        // ── Suspended Reasons ───────────────────────────────────

        public static List<CalendarSuspendedReasonDTO> ParseCalendarSuspendedReasons(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "suspended.reasons");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarSuspendedReasonsExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarSuspendedReasonDTO> result = new List<CalendarSuspendedReasonDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarSuspendedReasonDTO
                {
                    Id = ParseHelpers.GetIntOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedReasonsExpectedColumns[0].SourceIndex]),
                    Title = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSuspendedReasonsExpectedColumns[1].SourceIndex])
                });
            }

            return result;
        }

        // ── Security Changes ────────────────────────────────────

        public static List<CalendarSecurityChangeDTO> ParseCalendarSecurityChanges(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "securities");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarSecurityChangeDTO> result = new List<CalendarSecurityChangeDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarSecurityChangeDTO
                {
                    UpdateTime = ParseHelpers.GetDateTimeOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns[0].SourceIndex]),
                    Action = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns[1].SourceIndex]),
                    SecId = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns[2].SourceIndex]),
                    AttributeName = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns[3].SourceIndex]),
                    BeforeValue = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns[4].SourceIndex]),
                    AfterValue = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityChangesExpectedColumns[5].SourceIndex])
                });
            }

            return result;
        }

        // ── Security Attributes ─────────────────────────────────

        public static List<CalendarSecurityAttributeDTO> ParseCalendarSecurityAttributes(JsonDocument jsonDocument)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, "securities.attributes");
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarSecurityAttributesExpectedColumns);
            JsonElement data = table.GetProperty("data");

            List<CalendarSecurityAttributeDTO> result = new List<CalendarSecurityAttributeDTO>(data.GetArrayLength());

            for (int i = 0; i < data.GetArrayLength(); i++)
            {
                result.Add(new CalendarSecurityAttributeDTO
                {
                    Name = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityAttributesExpectedColumns[0].SourceIndex]),
                    Type = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityAttributesExpectedColumns[1].SourceIndex]),
                    Title = ParseHelpers.GetStringOrNull(data[i][ColumnAndNumbersForParsing.CalendarSecurityAttributesExpectedColumns[2].SourceIndex])
                });
            }

            return result;
        }

        // ── Cursor (универсальный для Calendar) ─────────────────

        /// <summary>
        /// Парсинг cursor-пагинации для Calendar endpoint'ов.
        ///
        /// cursorKey — имя JSON-таблицы с курсором.
        /// Примеры: "suspended.cursor", "securities.cursor".
        ///
        /// Структура всегда одинаковая: INDEX, TOTAL, PAGESIZE.
        /// </summary>
        public static PaginationCursorDTO ParseCursor(JsonDocument jsonDocument, string cursorKey)
        {
            JsonElement root = jsonDocument.RootElement;
            JsonElement table = GetTable(root, cursorKey);
            JsonElement columns = table.GetProperty("columns");
            ParseHelpers.ValidateColumns(columns, ColumnAndNumbersForParsing.CalendarCursorExpectedColumns);

            JsonElement datas = table.GetProperty("data");

            return new PaginationCursorDTO()
            {
                Index = ParseHelpers.GetIntOrNull(datas[0][ColumnAndNumbersForParsing.CalendarCursorExpectedColumns[0].SourceIndex]),
                Total = ParseHelpers.GetIntOrNull(datas[0][ColumnAndNumbersForParsing.CalendarCursorExpectedColumns[1].SourceIndex]),
                PageSize = ParseHelpers.GetIntOrNull(datas[0][ColumnAndNumbersForParsing.CalendarCursorExpectedColumns[2].SourceIndex])
            };
        }

        // ── Инфраструктура ──────────────────────────────────────

        private static JsonElement GetTable(JsonElement root, string tableName)
        {
            if (!root.TryGetProperty(tableName, out JsonElement table))
            {
                throw new InvalidOperationException(
                    $"MOEX ISS Calendar response does not contain table '{tableName}'.");
            }

            return table;
        }
        */
    }
}
