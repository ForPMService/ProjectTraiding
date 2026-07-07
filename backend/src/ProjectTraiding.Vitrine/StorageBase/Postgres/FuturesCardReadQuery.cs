using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class FuturesCardReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<FuturesCardReadQuery> _logger;

        public FuturesCardReadQuery(NpgsqlDataSource dataSource, ILogger<FuturesCardReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<VitrineFuturesCardDto?> GetBySecidAsync(string secid, CancellationToken ct)
        {
            const string table = "moex_futures_details";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT secid, boardid, shortname, secname, asset_code, initial_margin, minstep,
                           stepprice, lotvolume, decimals, last_trade_date, last_del_date,
                           high_limit, low_limit, buysell_fee
                    FROM moex_futures_details
                    WHERE secid = @secid
                    """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                VitrineFuturesCardDto? card = null;
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    card = new VitrineFuturesCardDto(
                        Secid: reader.GetString(0),
                        Boardid: reader.GetString(1),
                        Shortname: reader.IsDBNull(2) ? null : reader.GetString(2),
                        Secname: reader.IsDBNull(3) ? null : reader.GetString(3),
                        AssetCode: reader.IsDBNull(4) ? null : reader.GetString(4),
                        InitialMargin: reader.IsDBNull(5) ? null : (decimal?)reader.GetDecimal(5),
                        Minstep: reader.IsDBNull(6) ? null : (decimal?)reader.GetDecimal(6),
                        Stepprice: reader.IsDBNull(7) ? null : (decimal?)reader.GetDecimal(7),
                        Lotvolume: reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8),
                        Decimals: reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9),
                        LastTradeDate: reader.IsDBNull(10) ? null : (DateOnly?)reader.GetFieldValue<DateOnly>(10),
                        LastDelDate: reader.IsDBNull(11) ? null : (DateOnly?)reader.GetFieldValue<DateOnly>(11),
                        HighLimit: reader.IsDBNull(12) ? null : (decimal?)reader.GetDecimal(12),
                        LowLimit: reader.IsDBNull(13) ? null : (decimal?)reader.GetDecimal(13),
                        BuysellFee: reader.IsDBNull(14) ? null : (decimal?)reader.GetDecimal(14));
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                VitrineReadLogMessages.ReadCompleted(_logger, table, card is null ? 0 : 1, elapsed);
                return card;
            }
            catch (Exception ex)
            {
                VitrineReadLogMessages.ReadFailed(_logger, ex, table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<List<VitrineFuturesCardDto>> GetAllAsync(CancellationToken ct)
        {
            const string table = "moex_futures_details";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT secid, boardid, shortname, secname, asset_code, initial_margin, minstep,
                           stepprice, lotvolume, decimals, last_trade_date, last_del_date,
                           high_limit, low_limit, buysell_fee
                    FROM moex_futures_details
                    ORDER BY secid
                    """, connection);

                List<VitrineFuturesCardDto> result = new();
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new VitrineFuturesCardDto(
                        Secid: reader.GetString(0),
                        Boardid: reader.GetString(1),
                        Shortname: reader.IsDBNull(2) ? null : reader.GetString(2),
                        Secname: reader.IsDBNull(3) ? null : reader.GetString(3),
                        AssetCode: reader.IsDBNull(4) ? null : reader.GetString(4),
                        InitialMargin: reader.IsDBNull(5) ? null : (decimal?)reader.GetDecimal(5),
                        Minstep: reader.IsDBNull(6) ? null : (decimal?)reader.GetDecimal(6),
                        Stepprice: reader.IsDBNull(7) ? null : (decimal?)reader.GetDecimal(7),
                        Lotvolume: reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8),
                        Decimals: reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9),
                        LastTradeDate: reader.IsDBNull(10) ? null : (DateOnly?)reader.GetFieldValue<DateOnly>(10),
                        LastDelDate: reader.IsDBNull(11) ? null : (DateOnly?)reader.GetFieldValue<DateOnly>(11),
                        HighLimit: reader.IsDBNull(12) ? null : (decimal?)reader.GetDecimal(12),
                        LowLimit: reader.IsDBNull(13) ? null : (decimal?)reader.GetDecimal(13),
                        BuysellFee: reader.IsDBNull(14) ? null : (decimal?)reader.GetDecimal(14)));
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                VitrineReadLogMessages.ReadCompleted(_logger, table, result.Count, elapsed);
                return result;
            }
            catch (Exception ex)
            {
                VitrineReadLogMessages.ReadFailed(_logger, ex, table, ex.GetType().Name);
                throw;
            }
        }
    }
}
