using Microsoft.Extensions.Hosting;
using ProjectTraiding.Moex.StorageBase.Postgres;

namespace ProjectTraiding.Moex.Deletion
{
    /// <summary>
    /// Одна дорожка очереди удаления данных инструмента. Захват выполняется до создания
    /// области, а отказ заявки возвращает её в очередь с задержкой интервала опроса.
    /// </summary>
    public sealed class InstrumentDeletionBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly InstrumentDeletionQueueReader _queueReader;
        private readonly ILogger<InstrumentDeletionBackgroundService> _logger;
        private readonly TimeSpan _pollInterval;

        public InstrumentDeletionBackgroundService(
            IServiceScopeFactory scopeFactory,
            InstrumentDeletionQueueReader queueReader,
            ILogger<InstrumentDeletionBackgroundService> logger,
            TimeSpan pollInterval)
        {
            _scopeFactory = scopeFactory;
            _queueReader = queueReader;
            _logger = logger;
            _pollInterval = pollInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _queueReader.ReleaseInterruptedClaimsAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool worked;
                try
                {
                    worked = await TryRunOneAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Instrument deletion background poll failed unexpectedly.");
                    worked = false;
                }

                if (!worked)
                {
                    try
                    {
                        await Task.Delay(_pollInterval, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }

        private async Task<bool> TryRunOneAsync(CancellationToken ct)
        {
            InstrumentDeletionClaim? claim = await _queueReader.ClaimNextAsync(ct);
            if (claim is null)
                return false;

            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                InstrumentDataDeletionRunner runner = scope.ServiceProvider
                    .GetRequiredService<InstrumentDataDeletionRunner>();
                DeletionOutcome outcome = await runner.RunClaimedAsync(claim.Value.Id, claim.Value.Secid, ct);

                if (outcome.Status != DeletionStatus.Done)
                {
                    await _queueReader.DeferAsync(
                        claim.Value.Id,
                        RejectionText(outcome.Status),
                        _pollInterval,
                        ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Instrument deletion failed and will be retried: deletionId={DeletionId}, secid={Secid}.",
                    claim.Value.Id,
                    claim.Value.Secid);
                await _queueReader.DeferAsync(claim.Value.Id, ex.Message, _pollInterval, ct);
            }

            return true;
        }

        private static string RejectionText(DeletionStatus status) => status switch
        {
            DeletionStatus.LoadRunning => "по инструменту сейчас выполняется загрузка данных, удаление невозможно",
            DeletionStatus.RealtimeEnabled => "по инструменту включён приём реального времени, удаление невозможно",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}
