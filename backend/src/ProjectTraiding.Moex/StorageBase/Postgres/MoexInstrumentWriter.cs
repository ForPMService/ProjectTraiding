using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexInstrumentWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<MoexInstrumentWriter> _logger;

        public MoexInstrumentWriter(NpgsqlDataSource dataSource, ILogger<MoexInstrumentWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<DbWriteResult> UpsertStocksAsync(
            IReadOnlyList<StockInstrumentCardDTO> stocks,
            CancellationToken ct)
        {
            const string table = "moex_instruments/moex_stock_details (stock)";          
            MoexWriterLogMessages.WriteStarted(_logger, table, stocks.Count);            
            long startTs = Stopwatch.GetTimestamp();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            string currentKey = "?";
            int processedCount = 0;

            try
            {
                string[] secIds = new string[stocks.Count];
                string[] boardIds = new string[stocks.Count];
                string[] shortNames = new string[stocks.Count];
                string[] secNames = new string[stocks.Count];
                string?[] secTypes = new string?[stocks.Count];
                string?[] isins = new string?[stocks.Count];
                int?[] lotSizes = new int?[stocks.Count];
                double?[] minSteps = new double?[stocks.Count];
                int?[] decimalsValues = new int?[stocks.Count];
                string?[] currencies = new string?[stocks.Count];
                long?[] issueSizes = new long?[stocks.Count];
                int?[] listLevels = new int?[stocks.Count];
                string?[] statuses = new string?[stocks.Count];

                for (int i = 0; i < stocks.Count; i++)
                {
                    StockInstrumentCardDTO stock = stocks[i];
                    currentKey = stock.SecId ?? "?";
                    // ── проверка NOT NULL полей до SQL ──
                    if (string.IsNullOrWhiteSpace(stock.SecId))
                        throw new InvalidOperationException("SecId пустой");
                    if (string.IsNullOrWhiteSpace(stock.BoardId))
                        throw new InvalidOperationException($"BoardId пустой у {stock.SecId}");
                    if (string.IsNullOrWhiteSpace(stock.ShortName))
                        throw new InvalidOperationException($"ShortName пустой у {stock.SecId}");
                    if (string.IsNullOrWhiteSpace(stock.SecName))
                        throw new InvalidOperationException($"SecName пустой у {stock.SecId}");

                    secIds[i] = stock.SecId;
                    boardIds[i] = stock.BoardId;
                    shortNames[i] = stock.ShortName;
                    secNames[i] = stock.SecName;
                    secTypes[i] = stock.SecType;
                    isins[i] = stock.Isin;
                    lotSizes[i] = stock.LotSize;
                    minSteps[i] = stock.MinStep;
                    decimalsValues[i] = stock.Decimals;
                    currencies[i] = stock.Currency;
                    issueSizes[i] = stock.IssueSize;
                    listLevels[i] = stock.ListLevel;
                    statuses[i] = stock.Status;

                    processedCount++;
                }

                // Дальше идут обращения к базе: конкретная строка в отказе больше не видна.
                currentKey = "<пачка>";

                // ── UPSERT 1: общая таблица moex_instruments ──
                await using NpgsqlCommand instrumentCommand = new NpgsqlCommand("""
                    INSERT INTO moex_instruments
                        (secid, instrument_type, asset_code, shortname, secname, updated_at)
                    SELECT s.secid, 'stock', NULL, s.shortname, s.secname, now()
                    FROM (
                        SELECT DISTINCT ON (t.secid) t.secid, t.shortname, t.secname
                        FROM unnest(@secid, @shortname, @secname)
                             WITH ORDINALITY AS t(secid, shortname, secname, ord)
                        ORDER BY t.secid, t.ord DESC
                    ) AS s
                    ON CONFLICT (secid) DO UPDATE SET
                        instrument_type = EXCLUDED.instrument_type,
                        asset_code      = EXCLUDED.asset_code,
                        shortname       = EXCLUDED.shortname,
                        secname         = EXCLUDED.secname,
                        updated_at      = now()
                    """, connection, transaction);

                instrumentCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
                instrumentCommand.Parameters.Add("@shortname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = shortNames;
                instrumentCommand.Parameters.Add("@secname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secNames;

                await instrumentCommand.ExecuteNonQueryAsync(ct);

                // ── UPSERT 2: детали moex_stock_details ──
                await using NpgsqlCommand detailsCommand = new NpgsqlCommand("""
                    INSERT INTO moex_stock_details
                        (secid, boardid, shortname, secname, sectype, isin, lotsize,
                         minstep, decimals, currency_id, issue_size, list_level, status, updated_at)
                    SELECT s.secid, s.boardid, s.shortname, s.secname, s.sectype, s.isin, s.lotsize,
                           s.minstep, s.decimals, s.currency_id, s.issue_size, s.list_level, s.status, now()
                    FROM (
                        SELECT DISTINCT ON (t.secid)
                               t.secid, t.boardid, t.shortname, t.secname, t.sectype, t.isin, t.lotsize,
                               t.minstep, t.decimals, t.currency_id, t.issue_size, t.list_level, t.status
                        FROM unnest(@secid, @boardid, @shortname, @secname, @sectype, @isin, @lotsize,
                                    @minstep, @decimals, @currency_id, @issue_size, @list_level, @status)
                             WITH ORDINALITY
                             AS t(secid, boardid, shortname, secname, sectype, isin, lotsize,
                                  minstep, decimals, currency_id, issue_size, list_level, status, ord)
                        ORDER BY t.secid, t.ord DESC
                    ) AS s
                    ON CONFLICT (secid) DO UPDATE SET
                        boardid     = EXCLUDED.boardid,
                        shortname   = EXCLUDED.shortname,
                        secname     = EXCLUDED.secname,
                        sectype     = EXCLUDED.sectype,
                        isin        = EXCLUDED.isin,
                        lotsize     = EXCLUDED.lotsize,
                        minstep     = EXCLUDED.minstep,
                        decimals    = EXCLUDED.decimals,
                        currency_id = EXCLUDED.currency_id,
                        issue_size  = EXCLUDED.issue_size,
                        list_level  = EXCLUDED.list_level,
                        status      = EXCLUDED.status,
                        updated_at  = now()
                    """, connection, transaction);

                detailsCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
                detailsCommand.Parameters.Add("@boardid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = boardIds;
                detailsCommand.Parameters.Add("@shortname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = shortNames;
                detailsCommand.Parameters.Add("@secname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secNames;
                detailsCommand.Parameters.Add("@sectype", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secTypes;
                detailsCommand.Parameters.Add("@isin", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = isins;
                detailsCommand.Parameters.Add("@lotsize", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = lotSizes;
                detailsCommand.Parameters.Add("@minstep", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = minSteps;
                detailsCommand.Parameters.Add("@decimals", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = decimalsValues;
                detailsCommand.Parameters.Add("@currency_id", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = currencies;
                detailsCommand.Parameters.Add("@issue_size", NpgsqlDbType.Array | NpgsqlDbType.Bigint).Value = issueSizes;
                detailsCommand.Parameters.Add("@list_level", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = listLevels;
                detailsCommand.Parameters.Add("@status", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = statuses;

                await detailsCommand.ExecuteNonQueryAsync(ct);

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                MoexWriterLogMessages.WriteCompleted(_logger, table, processedCount, elapsed);
                return new DbWriteResult(stocks.Count, processedCount, elapsed);
            }
            catch(Exception ex)
            {
                MoexWriterLogMessages.WriteRolledBack(_logger, ex, table, currentKey, processedCount, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

    public async Task<DbWriteResult> UpsertFuturesAsync(
        IReadOnlyList<FuturesInstrumentCardDTO> futures,
        CancellationToken ct)
        {
            const string table = "moex_instruments/moex_futures_details (futures)";
            MoexWriterLogMessages.WriteStarted(_logger, table, futures.Count);
            long startTs = Stopwatch.GetTimestamp();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            string currentKey = "?";
            int processedCount = 0;

            // Серии срочных контрактов из этой партии: код серии -> код базового актива.
            Dictionary<string, string?> seriesByCode = new(StringComparer.Ordinal);

            try
            {
                string[] secIds = new string[futures.Count];
                string[] boardIds = new string[futures.Count];
                string[] shortNames = new string[futures.Count];
                string[] secNames = new string[futures.Count];
                string?[] secTypes = new string?[futures.Count];
                string?[] assetCodes = new string?[futures.Count];
                double?[] initialMargins = new double?[futures.Count];
                double?[] minSteps = new double?[futures.Count];
                double?[] stepPrices = new double?[futures.Count];
                int?[] lotVolumes = new int?[futures.Count];
                int?[] decimalsValues = new int?[futures.Count];
                DateOnly?[] lastTradeDates = new DateOnly?[futures.Count];
                DateOnly?[] lastDelDates = new DateOnly?[futures.Count];
                double?[] highLimits = new double?[futures.Count];
                double?[] lowLimits = new double?[futures.Count];
                double?[] buySellFees = new double?[futures.Count];

                for (int i = 0; i < futures.Count; i++)
                {
                    FuturesInstrumentCardDTO future = futures[i];
                    currentKey = future.SecId ?? "?";
                    // ── проверка NOT NULL полей до SQL ──
                    if (string.IsNullOrWhiteSpace(future.SecId))
                        throw new InvalidOperationException("SecId пустой");
                    if (string.IsNullOrWhiteSpace(future.BoardId))
                        throw new InvalidOperationException($"BoardId пустой у {future.SecId}");
                    if (string.IsNullOrWhiteSpace(future.ShortName))
                        throw new InvalidOperationException($"ShortName пустой у {future.SecId}");
                    if (string.IsNullOrWhiteSpace(future.SecName))
                        throw new InvalidOperationException($"SecName пустой у {future.SecId}");

                    secIds[i] = future.SecId;
                    boardIds[i] = future.BoardId;
                    shortNames[i] = future.ShortName;
                    secNames[i] = future.SecName;
                    secTypes[i] = future.SecType;
                    assetCodes[i] = future.AssetCode;
                    initialMargins[i] = future.InitialMargin;
                    minSteps[i] = future.MinStep;
                    stepPrices[i] = future.StepPrice;
                    lotVolumes[i] = future.LotVolume;
                    decimalsValues[i] = future.Decimals;
                    lastTradeDates[i] = ParseNullableDate(
                        future.LastTradeDate, table, "last_trade_date", future.SecId);
                    lastDelDates[i] = ParseNullableDate(
                        future.LastDelDate, table, "last_del_date", future.SecId);
                    highLimits[i] = future.HighLimit;
                    lowLimits[i] = future.LowLimit;
                    buySellFees[i] = future.BuySellFee;

                    // Бессрочный фьючерс серии не имеет: открытый интерес по нему источник
                    // отдаёт по собственному коду, а по коду серии возвращает пустой набор.
                    if (!string.IsNullOrWhiteSpace(future.SecType)
                        && !IsPerpetual(future.LastTradeDate))
                    {
                        seriesByCode[future.SecType] = future.AssetCode;
                    }

                    processedCount++;
                }

                // Дальше идут обращения к базе: конкретная строка в отказе больше не видна.
                currentKey = "<пачка контрактов>";

                // ── UPSERT 1: общая таблица moex_instruments ──
                await using NpgsqlCommand instrumentCommand = new NpgsqlCommand("""
                    INSERT INTO moex_instruments
                        (secid, instrument_type, asset_code, shortname, secname, updated_at)
                    SELECT s.secid, 'futures', s.asset_code, s.shortname, s.secname, now()
                    FROM (
                        SELECT DISTINCT ON (t.secid) t.secid, t.asset_code, t.shortname, t.secname
                        FROM unnest(@secid, @asset_code, @shortname, @secname)
                             WITH ORDINALITY AS t(secid, asset_code, shortname, secname, ord)
                        ORDER BY t.secid, t.ord DESC
                    ) AS s
                    ON CONFLICT (secid) DO UPDATE SET
                        instrument_type = EXCLUDED.instrument_type,
                        asset_code      = EXCLUDED.asset_code,
                        shortname       = EXCLUDED.shortname,
                        secname         = EXCLUDED.secname,
                        updated_at      = now()
                    """, connection, transaction);

                instrumentCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
                instrumentCommand.Parameters.Add("@asset_code", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = assetCodes;
                instrumentCommand.Parameters.Add("@shortname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = shortNames;
                instrumentCommand.Parameters.Add("@secname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secNames;

                await instrumentCommand.ExecuteNonQueryAsync(ct);

                // ── UPSERT 2: детали moex_futures_details ──
                await using NpgsqlCommand detailsCommand = new NpgsqlCommand("""
                    INSERT INTO moex_futures_details
                        (secid, boardid, shortname, secname, sectype, asset_code, initial_margin, minstep,
                         stepprice, lotvolume, decimals, last_trade_date, last_del_date,
                         high_limit, low_limit, buysell_fee, updated_at)
                    SELECT s.secid, s.boardid, s.shortname, s.secname, s.sectype, s.asset_code,
                           s.initial_margin, s.minstep, s.stepprice, s.lotvolume, s.decimals,
                           s.last_trade_date, s.last_del_date,
                           s.high_limit, s.low_limit, s.buysell_fee, now()
                    FROM (
                        SELECT DISTINCT ON (t.secid)
                               t.secid, t.boardid, t.shortname, t.secname, t.sectype, t.asset_code,
                               t.initial_margin, t.minstep, t.stepprice, t.lotvolume, t.decimals,
                               t.last_trade_date, t.last_del_date,
                               t.high_limit, t.low_limit, t.buysell_fee
                        FROM unnest(@secid, @boardid, @shortname, @secname, @sectype, @asset_code,
                                    @initial_margin, @minstep, @stepprice, @lotvolume, @decimals,
                                    @last_trade_date, @last_del_date,
                                    @high_limit, @low_limit, @buysell_fee)
                             WITH ORDINALITY
                             AS t(secid, boardid, shortname, secname, sectype, asset_code,
                                  initial_margin, minstep, stepprice, lotvolume, decimals,
                                  last_trade_date, last_del_date,
                                  high_limit, low_limit, buysell_fee, ord)
                        ORDER BY t.secid, t.ord DESC
                    ) AS s
                    ON CONFLICT (secid) DO UPDATE SET
                        boardid         = EXCLUDED.boardid,
                        shortname       = EXCLUDED.shortname,
                        secname         = EXCLUDED.secname,
                        sectype         = EXCLUDED.sectype,
                        asset_code      = EXCLUDED.asset_code,
                        initial_margin  = EXCLUDED.initial_margin,
                        minstep         = EXCLUDED.minstep,
                        stepprice       = EXCLUDED.stepprice,
                        lotvolume       = EXCLUDED.lotvolume,
                        decimals        = EXCLUDED.decimals,
                        last_trade_date = EXCLUDED.last_trade_date,
                        last_del_date   = EXCLUDED.last_del_date,
                        high_limit      = EXCLUDED.high_limit,
                        low_limit       = EXCLUDED.low_limit,
                        buysell_fee     = EXCLUDED.buysell_fee,
                        updated_at      = now()
                    """, connection, transaction);

                detailsCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
                detailsCommand.Parameters.Add("@boardid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = boardIds;
                detailsCommand.Parameters.Add("@shortname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = shortNames;
                detailsCommand.Parameters.Add("@secname", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secNames;
                detailsCommand.Parameters.Add("@sectype", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secTypes;
                detailsCommand.Parameters.Add("@asset_code", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = assetCodes;
                detailsCommand.Parameters.Add("@initial_margin", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = initialMargins;
                detailsCommand.Parameters.Add("@minstep", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = minSteps;
                detailsCommand.Parameters.Add("@stepprice", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = stepPrices;
                detailsCommand.Parameters.Add("@lotvolume", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = lotVolumes;
                detailsCommand.Parameters.Add("@decimals", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = decimalsValues;
                detailsCommand.Parameters.Add("@last_trade_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = lastTradeDates;
                detailsCommand.Parameters.Add("@last_del_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = lastDelDates;
                detailsCommand.Parameters.Add("@high_limit", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = highLimits;
                detailsCommand.Parameters.Add("@low_limit", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = lowLimits;
                detailsCommand.Parameters.Add("@buysell_fee", NpgsqlDbType.Array | NpgsqlDbType.Numeric).Value = buySellFees;

                await detailsCommand.ExecuteNonQueryAsync(ct);

                // ── UPSERT 3: строки серий ──
                // Условие в ON CONFLICT защищает настоящий инструмент от затирания, если
                // код серии когда-либо совпадёт с кодом контракта.
                // Название серии собирается шаблоном в самом запросе, а не склейкой строк
                // на каждую серию. Отбор по последнему вхождению здесь не нужен: ключи
                // словаря серий уже неповторимы.
                currentKey = "<пачка серий>";

                string[] seriesCodes = new string[seriesByCode.Count];
                string?[] seriesAssetCodes = new string?[seriesByCode.Count];
                int seriesIndex = 0;
                foreach (KeyValuePair<string, string?> series in seriesByCode)
                {
                    seriesCodes[seriesIndex] = series.Key;
                    seriesAssetCodes[seriesIndex] = series.Value;
                    seriesIndex++;
                }

                await using NpgsqlCommand seriesCommand = new NpgsqlCommand("""
                    INSERT INTO moex_instruments
                        (secid, instrument_type, asset_code, shortname, secname, updated_at)
                    SELECT t.secid, 'futures_series', t.asset_code, t.secid,
                           'Серия срочных контрактов ' || t.secid, now()
                    FROM unnest(@secid, @asset_code) AS t(secid, asset_code)
                    ON CONFLICT (secid) DO UPDATE SET
                        asset_code = EXCLUDED.asset_code,
                        secname    = EXCLUDED.secname,
                        updated_at = now()
                    WHERE moex_instruments.instrument_type = 'futures_series'
                    """, connection, transaction);

                seriesCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = seriesCodes;
                seriesCommand.Parameters.Add("@asset_code", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = seriesAssetCodes;

                await seriesCommand.ExecuteNonQueryAsync(ct);

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                MoexWriterLogMessages.WriteCompleted(_logger, table, processedCount, elapsed);
                return new DbWriteResult(futures.Count, processedCount, elapsed);
            }
            catch(Exception ex)
            {
                MoexWriterLogMessages.WriteRolledBack(_logger, ex, table, currentKey,processedCount, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        /// <summary>
        /// Бессрочный фьючерс: дата последнего торгового дня не наступает.
        /// Биржа обозначает это условным значением 2100-01-01.
        /// </summary>
        private static bool IsPerpetual(string? lastTradeDate)
        {
            if (string.IsNullOrWhiteSpace(lastTradeDate))
                return false;

            return DateOnly.TryParse(lastTradeDate, out DateOnly parsed)
                && parsed >= new DateOnly(2100, 1, 1);
        }

        // Значение уходит в массив параметров, поэтому пустота выражается пустым значением
        // типа, а не признаком отсутствия для отдельной команды. Событие неудачного разбора
        // и его поля прежние.
        private DateOnly? ParseNullableDate(string? raw, string table, string field, string key)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateOnly.TryParse(raw, out DateOnly parsed))
            {
                return parsed;
            }

            MoexWriterLogMessages.DateParseFailed(_logger, table, field, key, raw);
            return null;
        }
    }
}
