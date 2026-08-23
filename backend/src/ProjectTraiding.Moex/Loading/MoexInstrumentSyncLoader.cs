using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Loading
{
    /// <summary>
    /// Синхронизация справочника инструментов с биржей: обращение к источнику и запись
    /// карточек. Команду подаёт оператор через контур управления, последовательность
    /// принадлежит владельцу данных. Операция быстрая и выполняется в рамках запроса.
    /// </summary>
    public sealed class MoexInstrumentSyncLoader
    {
        private readonly MoexHttpIssClient _issClient;
        private readonly MoexHttpAlgClient _algClient;
        private readonly MoexInstrumentWriter _writer;

        public MoexInstrumentSyncLoader(
            MoexHttpIssClient issClient,
            MoexHttpAlgClient algClient,
            MoexInstrumentWriter writer)
        {
            _issClient = issClient;
            _algClient = algClient;
            _writer = writer;
        }

        public async Task<DbWriteResult> LoadStockAsync(CancellationToken ct)
        {
            List<StockInstrumentCardDTO> cards = await _issClient.GetStockInstrumentCards(ct);
            return await _writer.UpsertStocksAsync(cards, ct);
        }

        public async Task<DbWriteResult> LoadFuturesAsync(CancellationToken ct)
        {
            List<FuturesInstrumentCardDTO> cards = await _algClient.GetFuturesInstrumentCards(ct);
            return await _writer.UpsertFuturesAsync(cards, ct);
        }
    }
}
