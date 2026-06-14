using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.Storage.Postgres
{
    public sealed class MoexInstrumentWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexInstrumentWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task UpsertStocksAsync(
            IReadOnlyList<StockInstrumentCardDTO> stocks,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                foreach (StockInstrumentCardDTO stock in stocks)
                {
                    // ── проверка NOT NULL полей до SQL ──
                    if (string.IsNullOrWhiteSpace(stock.SecId))
                        throw new InvalidOperationException("SecId пустой");
                    if (string.IsNullOrWhiteSpace(stock.BoardId))
                        throw new InvalidOperationException($"BoardId пустой у {stock.SecId}");
                    if (string.IsNullOrWhiteSpace(stock.ShortName))
                        throw new InvalidOperationException($"ShortName пустой у {stock.SecId}");
                    if (string.IsNullOrWhiteSpace(stock.SecName))
                        throw new InvalidOperationException($"SecName пустой у {stock.SecId}");

                    // ── UPSERT 1: общая таблица moex_instruments ──
                    await using NpgsqlCommand instrumentCommand = new NpgsqlCommand("""
                INSERT INTO moex_instruments
                    (secid, instrument_type, asset_code, shortname, secname, updated_at)
                VALUES
                    (@secid, @instrument_type, @asset_code, @shortname, @secname, now())
                ON CONFLICT (secid) DO UPDATE SET
                    instrument_type = EXCLUDED.instrument_type,
                    asset_code      = EXCLUDED.asset_code,
                    shortname       = EXCLUDED.shortname,
                    secname         = EXCLUDED.secname,
                    updated_at      = now()
                """, connection, transaction);

                    instrumentCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = stock.SecId;
                    instrumentCommand.Parameters.Add("@instrument_type", NpgsqlDbType.Text).Value = "stock";
                    instrumentCommand.Parameters.Add("@asset_code", NpgsqlDbType.Text).Value = DBNull.Value;
                    instrumentCommand.Parameters.Add("@shortname", NpgsqlDbType.Text).Value = stock.ShortName;
                    instrumentCommand.Parameters.Add("@secname", NpgsqlDbType.Text).Value = stock.SecName;

                    await instrumentCommand.ExecuteNonQueryAsync(ct);

                    // ── UPSERT 2: детали moex_stock_details ──
                    await using NpgsqlCommand detailsCommand = new NpgsqlCommand("""
                INSERT INTO moex_stock_details
                    (secid, boardid, shortname, secname, sectype, isin, lotsize,
                     minstep, decimals, currency_id, issue_size, list_level, status, updated_at)
                VALUES
                    (@secid, @boardid, @shortname, @secname, @sectype, @isin, @lotsize,
                     @minstep, @decimals, @currency_id, @issue_size, @list_level, @status, now())
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

                    detailsCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = stock.SecId;
                    detailsCommand.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = stock.BoardId;
                    detailsCommand.Parameters.Add("@shortname", NpgsqlDbType.Text).Value = (object?)stock.ShortName ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@secname", NpgsqlDbType.Text).Value = (object?)stock.SecName ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@sectype", NpgsqlDbType.Text).Value = (object?)stock.SecType ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@isin", NpgsqlDbType.Text).Value = (object?)stock.Isin ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@lotsize", NpgsqlDbType.Integer).Value = (object?)stock.LotSize ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@minstep", NpgsqlDbType.Numeric).Value = (object?)stock.MinStep ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@decimals", NpgsqlDbType.Integer).Value = (object?)stock.Decimals ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@currency_id", NpgsqlDbType.Text).Value = (object?)stock.Currency ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@issue_size", NpgsqlDbType.Bigint).Value = (object?)stock.IssueSize ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@list_level", NpgsqlDbType.Integer).Value = (object?)stock.ListLevel ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@status", NpgsqlDbType.Text).Value = (object?)stock.Status ?? DBNull.Value;

                    await detailsCommand.ExecuteNonQueryAsync(ct);

                }
                await transaction.CommitAsync(ct);

            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

    public async Task UpsertFuturesAsync(
        IReadOnlyList<FuturesInstrumentCardDTO> futures,
        CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                foreach (FuturesInstrumentCardDTO future in futures)
                {
                    // ── проверка NOT NULL полей до SQL ──
                    if (string.IsNullOrWhiteSpace(future.SecId))
                        throw new InvalidOperationException("SecId пустой");
                    if (string.IsNullOrWhiteSpace(future.BoardId))
                        throw new InvalidOperationException($"BoardId пустой у {future.SecId}");
                    if (string.IsNullOrWhiteSpace(future.ShortName))
                        throw new InvalidOperationException($"ShortName пустой у {future.SecId}");
                    if (string.IsNullOrWhiteSpace(future.SecName))
                        throw new InvalidOperationException($"SecName пустой у {future.SecId}");

                    // ── UPSERT 1: общая таблица moex_instruments ──
                    await using NpgsqlCommand instrumentCommand = new NpgsqlCommand("""
                        INSERT INTO moex_instruments
                            (secid, instrument_type, asset_code, shortname, secname, updated_at)
                        VALUES
                            (@secid, @instrument_type, @asset_code, @shortname, @secname, now())
                        ON CONFLICT (secid) DO UPDATE SET
                            instrument_type = EXCLUDED.instrument_type,
                            asset_code      = EXCLUDED.asset_code,
                            shortname       = EXCLUDED.shortname,
                            secname         = EXCLUDED.secname,
                            updated_at      = now()
                        """, connection, transaction);

                    instrumentCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = future.SecId;
                    instrumentCommand.Parameters.Add("@instrument_type", NpgsqlDbType.Text).Value = "futures";
                    instrumentCommand.Parameters.Add("@asset_code", NpgsqlDbType.Text).Value = (object?)future.AssetCode ?? DBNull.Value;
                    instrumentCommand.Parameters.Add("@shortname", NpgsqlDbType.Text).Value = future.ShortName;
                    instrumentCommand.Parameters.Add("@secname", NpgsqlDbType.Text).Value = future.SecName;

                    await instrumentCommand.ExecuteNonQueryAsync(ct);

                    // ── UPSERT 2: детали moex_futures_details ──
                    await using NpgsqlCommand detailsCommand = new NpgsqlCommand("""
                        INSERT INTO moex_futures_details
                            (secid, boardid, shortname, secname, asset_code, initial_margin, minstep,
                             stepprice, lotvolume, decimals, last_trade_date, last_del_date,
                             high_limit, low_limit, buysell_fee, updated_at)
                        VALUES
                            (@secid, @boardid, @shortname, @secname, @asset_code, @initial_margin, @minstep,
                             @stepprice, @lotvolume, @decimals, @last_trade_date, @last_del_date,
                             @high_limit, @low_limit, @buysell_fee, now())
                        ON CONFLICT (secid) DO UPDATE SET
                            boardid         = EXCLUDED.boardid,
                            shortname       = EXCLUDED.shortname,
                            secname         = EXCLUDED.secname,
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

                    detailsCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = future.SecId;
                    detailsCommand.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = future.BoardId;
                    detailsCommand.Parameters.Add("@shortname", NpgsqlDbType.Text).Value = (object?)future.ShortName ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@secname", NpgsqlDbType.Text).Value = (object?)future.SecName ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@asset_code", NpgsqlDbType.Text).Value = (object?)future.AssetCode ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@initial_margin", NpgsqlDbType.Numeric).Value = (object?)future.InitialMargin ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@minstep", NpgsqlDbType.Numeric).Value = (object?)future.MinStep ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@stepprice", NpgsqlDbType.Numeric).Value = (object?)future.StepPrice ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@lotvolume", NpgsqlDbType.Integer).Value = (object?)future.LotVolume ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@decimals", NpgsqlDbType.Integer).Value = (object?)future.Decimals ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@last_trade_date", NpgsqlDbType.Text).Value = (object?)future.LastTradeDate ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@last_del_date", NpgsqlDbType.Text).Value = (object?)future.LastDelDate ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@high_limit", NpgsqlDbType.Numeric).Value = (object?)future.HighLimit ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@low_limit", NpgsqlDbType.Numeric).Value = (object?)future.LowLimit ?? DBNull.Value;
                    detailsCommand.Parameters.Add("@buysell_fee", NpgsqlDbType.Numeric).Value = (object?)future.BuySellFee ?? DBNull.Value;

                    await detailsCommand.ExecuteNonQueryAsync(ct);
                }

                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
