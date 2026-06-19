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
    public sealed class StockCardReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<StockCardReadQuery> _logger;

        public StockCardReadQuery(NpgsqlDataSource dataSource, ILogger<StockCardReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<VitrineStockCardDto?> GetBySecidAsync(string secid, CancellationToken ct)
        {
            const string table = "moex_stock_details";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT secid, boardid, shortname, secname, sectype, isin, lotsize, minstep,
                           decimals, currency_id, issue_size, list_level, status
                    FROM moex_stock_details
                    WHERE secid = @secid
                    """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                VitrineStockCardDto? card = null;
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    card = new VitrineStockCardDto(
                        Secid: reader.GetString(0),
                        Boardid: reader.GetString(1),
                        Shortname: reader.IsDBNull(2) ? null : reader.GetString(2),
                        Secname: reader.IsDBNull(3) ? null : reader.GetString(3),
                        Sectype: reader.IsDBNull(4) ? null : reader.GetString(4),
                        Isin: reader.IsDBNull(5) ? null : reader.GetString(5),
                        Lotsize: reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                        Minstep: reader.IsDBNull(7) ? null : (decimal?)reader.GetDecimal(7),
                        Decimals: reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8),
                        CurrencyId: reader.IsDBNull(9) ? null : reader.GetString(9),
                        IssueSize: reader.IsDBNull(10) ? null : (long?)reader.GetInt64(10),
                        ListLevel: reader.IsDBNull(11) ? null : (int?)reader.GetInt32(11),
                        Status: reader.IsDBNull(12) ? null : reader.GetString(12));
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
    }
}
