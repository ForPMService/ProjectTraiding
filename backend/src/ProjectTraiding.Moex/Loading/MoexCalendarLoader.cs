using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.StorageBase.Postgres;

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

        public MoexCalendarLoader(
            MoexHttpCalendarClient calendarClient,
            MoexHttpIssClient issClient,
            MoexHttpAlgClient algClient,
            MoexCalendarWriter calendarWriter,
            MoexInstrumentBoardIntervalWriter intervalWriter,
            MoexFuturesExpirationWriter expirationWriter,
            MoexSplitWriter splitWriter)
        {
            _calendarClient = calendarClient;
            _issClient = issClient;
            _algClient = algClient;
            _calendarWriter = calendarWriter;
            _intervalWriter = intervalWriter;
            _expirationWriter = expirationWriter;
            _splitWriter = splitWriter;
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
                expirations.AddRange(
                    await _calendarClient.GetFuturesSecurities(yearFrom, yearTill, ct));
                year++;
            }

            DbWriteResult result = await _expirationWriter.ReplaceRangeAsync(
                dateFrom, dateTill, expirations, ct);
            return result.RowsWritten;
        }

        public async Task<int> LoadSplitsAsync(CancellationToken ct)
        {
            List<SplitWriteDTO> splits = await _issClient.GetSplits(ct);
            if (splits.Count == 0)
                return 0;

            DateOnly minDate = DateOnly.MaxValue;
            DateOnly maxDate = DateOnly.MinValue;
            for (int index = 0; index < splits.Count; index++)
            {
                DateOnly tradeDate = splits[index].TradeDate;
                if (tradeDate < minDate)
                    minDate = tradeDate;
                if (tradeDate > maxDate)
                    maxDate = tradeDate;
            }

            DbWriteResult result = await _splitWriter.ReplaceRangeAsync(
                minDate, maxDate, splits, ct);
            return result.RowsWritten;
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
                DateOnly date = row.TradeDate;
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
                result[row.TradeDate] = new EngineDayTimes(row.StartTime, row.StopTime);
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
                tradeSessionDate = stockDay.TradeSessionDate;
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
                tradeSessionDate = futuresDay.TradeSessionDate;
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
                if (row.HistoryFrom is null)
                    continue;
                InstrumentBoardKey key = new InstrumentBoardKey(row.SecId, row.BoardId);
                output.Add(new InstrumentBoardIntervalDTO
                {
                    Market = market,
                    SecId = row.SecId,
                    BoardId = row.BoardId,
                    ValidFrom = row.HistoryFrom.Value,
                    ValidTill = active.Contains(key) ? null : row.HistoryTill,
                });
            }
        }

        private static int RequireIsTraded(CalendarOffDaysMarketDTO row, string market)
        {
            if (row.IsTraded is null)
                throw new InvalidOperationException($"Пустой is_traded в календаре {market}.");
            return row.IsTraded.Value;
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
