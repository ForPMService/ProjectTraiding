using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Globalization;

namespace ProjectTraiding.Moex.Loading
{
    public sealed class MoexCalendarLoader
    {
        private const int ListingPageSize = 1000;
        private static readonly DateOnly DefaultFrom = new DateOnly(2021, 1, 1);

        private readonly MoexHttpCalendarClient _calendarClient;
        private readonly MoexHttpIssClient _issClient;
        private readonly MoexHttpAlgClient _algClient;
        private readonly MoexCalendarWriter _calendarWriter;
        private readonly MoexInstrumentBoardIntervalWriter _intervalWriter;
        private readonly MoexFuturesExpirationWriter _expirationWriter;
        private readonly MoexSplitWriter _splitWriter;
        private readonly MoexTradingPeriodWriter _periodWriter;
        private readonly MoexTradingPeriodTypeWriter _periodTypeWriter;

        public MoexCalendarLoader(
            MoexHttpCalendarClient calendarClient,
            MoexHttpIssClient issClient,
            MoexHttpAlgClient algClient,
            MoexCalendarWriter calendarWriter,
            MoexInstrumentBoardIntervalWriter intervalWriter,
            MoexFuturesExpirationWriter expirationWriter,
            MoexSplitWriter splitWriter,
            MoexTradingPeriodWriter periodWriter,
            MoexTradingPeriodTypeWriter periodTypeWriter)
        {
            _calendarClient = calendarClient;
            _issClient = issClient;
            _algClient = algClient;
            _calendarWriter = calendarWriter;
            _intervalWriter = intervalWriter;
            _expirationWriter = expirationWriter;
            _splitWriter = splitWriter;
            _periodWriter = periodWriter;
            _periodTypeWriter = periodTypeWriter;
        }

        public static DateOnly GetDefaultDateFrom()
        {
            return DefaultFrom;
        }

        public async Task<int> LoadDaysAsync(
            DateOnly dateFrom,
            DateOnly dateTill,
            CancellationToken ct)
        {
            ValidateRange(dateFrom, dateTill);
            List<EngineDailyTableDTO> stockEngineRows = await _issClient.GetEngine("stock", ct);
            List<EngineDailyTableDTO> futuresEngineRows = await _issClient.GetEngine("futures", ct);
            Dictionary<DateOnly, EngineDayTimes> stockTimes = BuildEngineTimes(stockEngineRows, "stock");
            Dictionary<DateOnly, EngineDayTimes> futuresTimes = BuildEngineTimes(futuresEngineRows, "futures");
            List<CalendarDayWriteDTO> output = new List<CalendarDayWriteDTO>();

            int year = dateFrom.Year;
            while (year <= dateTill.Year)
            {
                DateOnly yearFrom = year == dateFrom.Year
                    ? dateFrom
                    : new DateOnly(year, 1, 1);
                DateOnly yearTill = year == dateTill.Year
                    ? dateTill
                    : new DateOnly(year, 12, 31);

                List<CalendarOffDaysMarketDTO> stockRows =
                    await _calendarClient.GetStockOffDays(yearFrom, yearTill, ct);
                List<CalendarOffDaysMarketDTO> futuresRows =
                    await _calendarClient.GetFuturesOffDays(yearFrom, yearTill, ct);
                Dictionary<DateOnly, CalendarOffDaysMarketDTO> stockActual =
                    BuildActualDays(stockRows, "stock");
                Dictionary<DateOnly, CalendarOffDaysMarketDTO> futuresActual =
                    BuildActualDays(futuresRows, "futures");

                DateOnly date = yearFrom;
                while (date <= yearTill)
                {
                    CalendarOffDaysMarketDTO? stockDay;
                    CalendarOffDaysMarketDTO? futuresDay;
                    bool hasStockDay = stockActual.TryGetValue(date, out stockDay);
                    bool hasFuturesDay = futuresActual.TryGetValue(date, out futuresDay);

                    output.Add(CreateStockDay(
                        date, hasStockDay ? stockDay : null,
                        hasFuturesDay ? futuresDay : null, stockTimes));
                    output.Add(CreateFuturesDay(
                        date, hasFuturesDay ? futuresDay : null, futuresTimes));
                    date = date.AddDays(1);
                }

                year++;
            }

            DbWriteResult result = await _calendarWriter.UpsertDaysAsync(output, ct);
            return result.RowsWritten;
        }

        public async Task<int> LoadIntervalsAsync(CancellationToken ct)
        {
            List<ListingIntervalDTO> stockListing =
                await LoadListingPagesAsync("stock", "shares", null, ct);
            List<ListingIntervalDTO> futuresListing =
                await LoadListingPagesAsync("futures", "forts", null, ct);
            List<ListingIntervalDTO> activeStockListing =
                await LoadListingPagesAsync("stock", "shares", "traded", ct);
            List<FuturesInstrumentCardDTO> activeFutures =
                await _algClient.GetFuturesInstrumentCards(ct);

            HashSet<InstrumentBoardKey> activeStock = new HashSet<InstrumentBoardKey>();
            for (int index = 0; index < activeStockListing.Count; index++)
            {
                ListingIntervalDTO row = activeStockListing[index];
                if (!string.IsNullOrWhiteSpace(row.SecId) && !string.IsNullOrWhiteSpace(row.BoardId))
                    activeStock.Add(new InstrumentBoardKey(row.SecId, row.BoardId));
            }

            HashSet<InstrumentBoardKey> activeFuturesKeys = new HashSet<InstrumentBoardKey>();
            for (int index = 0; index < activeFutures.Count; index++)
            {
                FuturesInstrumentCardDTO row = activeFutures[index];
                if (!string.IsNullOrWhiteSpace(row.SecId))
                    activeFuturesKeys.Add(new InstrumentBoardKey(row.SecId, "RFUD"));
            }

            List<InstrumentBoardIntervalDTO> intervals =
                new List<InstrumentBoardIntervalDTO>(stockListing.Count + futuresListing.Count);
            AppendIntervals(stockListing, "stock", activeStock, intervals);
            AppendIntervals(futuresListing, "futures", activeFuturesKeys, intervals);

            DbWriteResult result = await _intervalWriter.ReplaceAllAsync(intervals, ct);
            return result.RowsWritten;
        }

        public async Task<int> LoadExpirationsAsync(
            DateOnly dateFrom,
            DateOnly dateTill,
            CancellationToken ct)
        {
            ValidateRange(dateFrom, dateTill);
            List<FuturesExpirationDTO> expirations = new List<FuturesExpirationDTO>();

            int year = dateFrom.Year;
            while (year <= dateTill.Year)
            {
                DateOnly yearFrom = year == dateFrom.Year
                    ? dateFrom
                    : new DateOnly(year, 1, 1);
                DateOnly yearTill = year == dateTill.Year
                    ? dateTill
                    : new DateOnly(year, 12, 31);
                (List<CalendarFortsContractDTO> Forts, List<CalendarOptionsSeriesDTO> Options) response =
                    await _calendarClient.GetFuturesSecurities(yearFrom, yearTill, ct);

                for (int index = 0; index < response.Forts.Count; index++)
                {
                    CalendarFortsContractDTO contract = response.Forts[index];
                    if (string.IsNullOrWhiteSpace(contract.SecId))
                        throw new InvalidOperationException("Пустой secid в блоке forts.");
                    DateOnly expirationDate = ParseRequiredDate(
                        contract.ExpirationDate, "forts.expiration_date", contract.SecId);
                    expirations.Add(new FuturesExpirationDTO
                    {
                        SecId = contract.SecId,
                        AssetCode = contract.AssetCode,
                        ExpirationDate = expirationDate,
                        ExpirationType = contract.ExpirationType,
                        EndDate = ParseNullableDate(contract.EndDate, "forts.end_date", contract.SecId),
                        WeekendSession = ToNullableInt16(
                            contract.WeekendSession, "forts.weekend_session", contract.SecId),
                    });
                }
                year++;
            }

            DbWriteResult result = await _expirationWriter.ReplaceRangeAsync(
                dateFrom, dateTill, expirations, ct);
            return result.RowsWritten;
        }

        public async Task<int> LoadSplitsAsync(CancellationToken ct)
        {
            List<SplitDTO> sourceRows = await _issClient.GetSplits(ct);
            if (sourceRows.Count == 0)
                return 0;

            List<SplitWriteDTO> splits = new List<SplitWriteDTO>(sourceRows.Count);
            DateOnly minDate = DateOnly.MaxValue;
            DateOnly maxDate = DateOnly.MinValue;
            for (int index = 0; index < sourceRows.Count; index++)
            {
                SplitDTO row = sourceRows[index];
                if (string.IsNullOrWhiteSpace(row.SecId))
                    throw new InvalidOperationException("Пустой secid в блоке splits.");
                if (row.BeforeQty is null || row.AfterQty is null)
                    throw new InvalidOperationException($"Пустое отношение дробления у {row.SecId}.");
                DateOnly tradeDate = ParseRequiredDate(
                    row.TradeDate, "splits.tradedate", row.SecId);
                if (tradeDate < minDate)
                    minDate = tradeDate;
                if (tradeDate > maxDate)
                    maxDate = tradeDate;
                splits.Add(new SplitWriteDTO
                {
                    TradeDate = tradeDate,
                    SecId = row.SecId,
                    BeforeQty = row.BeforeQty.Value,
                    AfterQty = row.AfterQty.Value,
                });
            }

            DbWriteResult result = await _splitWriter.ReplaceRangeAsync(
                minDate, maxDate, splits, ct);
            return result.RowsWritten;
        }

        public async Task<int> SnapshotPeriodsAsync(CancellationToken ct)
        {
            DateTimeOffset snapshotAt = DateTimeOffset.UtcNow;
            (List<CalendarStockSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types) stock =
                await _calendarClient.GetStockSession(ct);
            (List<CalendarFuturesSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types) futures =
                await _calendarClient.GetFuturesSession(ct);

            List<TradingPeriodDTO> periods =
                new List<TradingPeriodDTO>(stock.Sessions.Count + futures.Sessions.Count);
            AppendStockPeriods(stock.Sessions, snapshotAt, periods);
            AppendFuturesPeriods(futures.Sessions, snapshotAt, periods);

            List<TradingPeriodTypeDTO> types =
                new List<TradingPeriodTypeDTO>(stock.Types.Count + futures.Types.Count);
            AppendPeriodTypes(stock.Types, "stock", types);
            AppendPeriodTypes(futures.Types, "futures", types);

            DbWriteResult periodResult = await _periodWriter.AppendAsync(periods, ct);
            DbWriteResult typeResult = await _periodTypeWriter.UpsertAsync(types, ct);
            return periodResult.RowsWritten + typeResult.RowsWritten;
        }

        private async Task<List<ListingIntervalDTO>> LoadListingPagesAsync(
            string engine,
            string market,
            string? status,
            CancellationToken ct)
        {
            List<ListingIntervalDTO> rows = new List<ListingIntervalDTO>();
            int start = 0;
            while (true)
            {
                List<ListingIntervalDTO> page = await _issClient.GetListing(
                    engine, market, status, ListingPageSize, start, ct);
                if (page.Count == 0)
                    break;
                rows.AddRange(page);
                start += page.Count;
            }
            return rows;
        }

        private static Dictionary<DateOnly, CalendarOffDaysMarketDTO> BuildActualDays(
            IReadOnlyList<CalendarOffDaysMarketDTO> rows,
            string market)
        {
            Dictionary<DateOnly, CalendarOffDaysMarketDTO> result =
                new Dictionary<DateOnly, CalendarOffDaysMarketDTO>();
            for (int index = 0; index < rows.Count; index++)
            {
                CalendarOffDaysMarketDTO row = rows[index];
                if (row.UpdateTime is null)
                    continue;
                DateOnly date = ParseRequiredDate(row.TradeDate, $"calendar.{market}.tradedate", market);
                result[date] = row;
            }
            return result;
        }

        private static Dictionary<DateOnly, EngineDayTimes> BuildEngineTimes(
            IReadOnlyList<EngineDailyTableDTO> rows,
            string market)
        {
            Dictionary<DateOnly, EngineDayTimes> result = new Dictionary<DateOnly, EngineDayTimes>();
            for (int index = 0; index < rows.Count; index++)
            {
                EngineDailyTableDTO row = rows[index];
                DateOnly date = ParseRequiredDate(row.TradeDate, $"dailytable.{market}.date", market);
                result[date] = new EngineDayTimes(
                    ParseNullableTime(row.StartTime, $"dailytable.{market}.start_time", date),
                    ParseNullableTime(row.StopTime, $"dailytable.{market}.stop_time", date));
            }
            return result;
        }

        private static CalendarDayWriteDTO CreateStockDay(
            DateOnly date,
            CalendarOffDaysMarketDTO? stockDay,
            CalendarOffDaysMarketDTO? futuresDay,
            Dictionary<DateOnly, EngineDayTimes> times)
        {
            int isTraded;
            DateOnly? tradeSessionDate = null;
            string? reason = null;
            string dataSource;
            DateTime? updateTime = null;
            if (stockDay is not null)
            {
                isTraded = RequireIsTraded(stockDay, "stock");
                tradeSessionDate = ParseNullableDate(
                    stockDay.TradeSessionDate, "calendar.stock.trade_session_date", date.ToString());
                reason = stockDay.Reason;
                dataSource = "calendar";
                updateTime = stockDay.UpdateTime;
            }
            else if (futuresDay is not null)
            {
                isTraded = RequireIsTraded(futuresDay, "futures");
                dataSource = "calendar_futures";
            }
            else
            {
                isTraded = IsWeekday(date) ? 1 : 0;
                dataSource = "weekday_rule";
            }

            EngineDayTimes engineTimes;
            times.TryGetValue(date, out engineTimes);
            return new CalendarDayWriteDTO
            {
                TradeDate = date,
                Market = "stock",
                IsTraded = isTraded,
                TradeSessionDate = tradeSessionDate,
                Reason = reason,
                StartTime = engineTimes.StartTime,
                StopTime = engineTimes.StopTime,
                DataSource = dataSource,
                MoexUpdateTime = updateTime,
            };
        }

        private static CalendarDayWriteDTO CreateFuturesDay(
            DateOnly date,
            CalendarOffDaysMarketDTO? futuresDay,
            Dictionary<DateOnly, EngineDayTimes> times)
        {
            int isTraded;
            DateOnly? tradeSessionDate = null;
            string? reason = null;
            string dataSource;
            DateTime? updateTime = null;
            if (futuresDay is not null)
            {
                isTraded = RequireIsTraded(futuresDay, "futures");
                tradeSessionDate = ParseNullableDate(
                    futuresDay.TradeSessionDate, "calendar.futures.trade_session_date", date.ToString());
                reason = futuresDay.Reason;
                dataSource = "calendar";
                updateTime = futuresDay.UpdateTime;
            }
            else
            {
                isTraded = IsWeekday(date) ? 1 : 0;
                dataSource = "weekday_rule";
            }

            EngineDayTimes engineTimes;
            times.TryGetValue(date, out engineTimes);
            return new CalendarDayWriteDTO
            {
                TradeDate = date,
                Market = "futures",
                IsTraded = isTraded,
                TradeSessionDate = tradeSessionDate,
                Reason = reason,
                StartTime = engineTimes.StartTime,
                StopTime = engineTimes.StopTime,
                DataSource = dataSource,
                MoexUpdateTime = updateTime,
            };
        }

        private static void AppendIntervals(
            IReadOnlyList<ListingIntervalDTO> source,
            string market,
            HashSet<InstrumentBoardKey> active,
            List<InstrumentBoardIntervalDTO> output)
        {
            for (int index = 0; index < source.Count; index++)
            {
                ListingIntervalDTO row = source[index];
                if (string.IsNullOrWhiteSpace(row.SecId) || string.IsNullOrWhiteSpace(row.BoardId))
                    throw new InvalidOperationException($"Пустая пара secid/boardid в listing ({market}).");
                InstrumentBoardKey key = new InstrumentBoardKey(row.SecId, row.BoardId);
                output.Add(new InstrumentBoardIntervalDTO
                {
                    Market = market,
                    SecId = row.SecId,
                    BoardId = row.BoardId,
                    ValidFrom = ParseRequiredDate(row.HistoryFrom, "listing.history_from", row.SecId),
                    ValidTill = active.Contains(key)
                        ? null
                        : ParseNullableDate(row.HistoryTill, "listing.history_till", row.SecId),
                });
            }
        }

        private static void AppendStockPeriods(
            IReadOnlyList<CalendarStockSessionDTO> source,
            DateTimeOffset snapshotAt,
            List<TradingPeriodDTO> output)
        {
            for (int index = 0; index < source.Count; index++)
            {
                CalendarStockSessionDTO row = source[index];
                DateOnly tradeDate = ParseRequiredDate(
                    row.TradeDate, "session_schedule.tradedate", "stock");
                string boardId = RequireString(row.BoardId, "session_schedule.boardid", "stock");
                string periodType = RequireString(row.Type, "session_schedule.type", boardId);
                TimeOnly timeFrom = ParseRequiredTime(
                    row.TimeFrom, "session_schedule.time_from", boardId);
                TimeOnly? timeTill = ParseNullableTime(
                    row.TimeTill, "session_schedule.time_till", tradeDate);
                output.Add(new TradingPeriodDTO
                {
                    Market = "stock",
                    TradeDate = tradeDate,
                    SnapshotAt = snapshotAt,
                    BoardId = boardId,
                    SecId = row.SecId ?? string.Empty,
                    Session = ToNullableInt16(row.TradingSession, "tradingsession", boardId),
                    PeriodType = periodType,
                    TimeFrom = tradeDate.ToDateTime(timeFrom),
                    TimeTill = timeTill.HasValue
                        ? tradeDate.ToDateTime(timeTill.Value)
                        : null,
                    MoexUpdateTime = row.UpdateTime,
                });
            }
        }

        private static void AppendFuturesPeriods(
            IReadOnlyList<CalendarFuturesSessionDTO> source,
            DateTimeOffset snapshotAt,
            List<TradingPeriodDTO> output)
        {
            for (int index = 0; index < source.Count; index++)
            {
                CalendarFuturesSessionDTO row = source[index];
                DateOnly tradeDate = ParseRequiredDate(
                    row.TradeSessionDate, "session_schedule.trade_session_date", "futures");
                string boardId = RequireString(row.BoardId, "session_schedule.boardid", "futures");
                string periodType = RequireString(row.Type, "session_schedule.type", boardId);
                if (row.TimeFrom is null)
                    throw new InvalidOperationException($"Пустое time_from у {boardId}.");
                output.Add(new TradingPeriodDTO
                {
                    Market = "futures",
                    TradeDate = tradeDate,
                    SnapshotAt = snapshotAt,
                    BoardId = boardId,
                    SecId = row.SecId ?? string.Empty,
                    PeriodType = periodType,
                    TimeFrom = row.TimeFrom.Value,
                    TimeTill = row.TimeTill,
                    MoexUpdateTime = row.UpdateTime,
                });
            }
        }

        private static void AppendPeriodTypes(
            IReadOnlyList<CalendarSessionTypeDTO> source,
            string market,
            List<TradingPeriodTypeDTO> output)
        {
            for (int index = 0; index < source.Count; index++)
            {
                CalendarSessionTypeDTO row = source[index];
                output.Add(new TradingPeriodTypeDTO
                {
                    Market = market,
                    TypeCode = RequireString(row.Type, "session_schedule.types.type", market),
                    Title = RequireString(row.Title, "session_schedule.types.title", market),
                });
            }
        }

        private static int RequireIsTraded(CalendarOffDaysMarketDTO row, string market)
        {
            if (row.IsTraded is null)
                throw new InvalidOperationException($"Пустой is_traded в календаре {market}.");
            return row.IsTraded.Value;
        }

        private static string RequireString(string? value, string field, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Пустое поле {field} у {key}.");
            return value;
        }

        private static DateOnly ParseRequiredDate(string? value, string field, string key)
        {
            if (value is not null
                && DateOnly.TryParseExact(
                    value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly parsed))
            {
                return parsed;
            }
            throw new InvalidOperationException($"Некорректная дата {field} у {key}: '{value}'.");
        }

        private static DateOnly? ParseNullableDate(string? value, string field, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return ParseRequiredDate(value, field, key);
        }

        private static TimeOnly ParseRequiredTime(string? value, string field, string key)
        {
            if (value is not null
                && TimeOnly.TryParseExact(
                    value, "HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out TimeOnly parsed))
            {
                return parsed;
            }
            throw new InvalidOperationException($"Некорректное время {field} у {key}: '{value}'.");
        }

        private static TimeOnly? ParseNullableTime(string? value, string field, object key)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return ParseRequiredTime(value, field, key.ToString() ?? "?");
        }

        private static short? ToNullableInt16(int? value, string field, string key)
        {
            if (value is null)
                return null;
            if (value.Value < short.MinValue || value.Value > short.MaxValue)
                throw new InvalidOperationException($"Значение {field} у {key} вне диапазона smallint.");
            return (short)value.Value;
        }

        private static bool IsWeekday(DateOnly date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday
                && date.DayOfWeek != DayOfWeek.Sunday;
        }

        private static void ValidateRange(DateOnly dateFrom, DateOnly dateTill)
        {
            if (dateFrom > dateTill)
                throw new ArgumentException("dateFrom не может быть позже dateTill.");
        }

        private readonly record struct EngineDayTimes(TimeOnly? StartTime, TimeOnly? StopTime);
        private readonly record struct InstrumentBoardKey(string SecId, string BoardId);
    }
}
