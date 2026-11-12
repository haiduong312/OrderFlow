using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;

namespace OrderFlow.Worker;

public sealed class RefundWorker(RefundProcessingIteration iteration, TimeProvider clock, ILogger<RefundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    for (var i = 0; i < 20; i++)
                        if (!await iteration.ProcessNextAsync(stoppingToken)) break;
                }
                catch (Exception failure) when (!stoppingToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
                {
                    logger.LogWarning(new EventId(1703, "RecoveryContention"),
                        "Recovery pass postponed with PostgreSQL state {SqlState}", OrderDatabase.Contention(failure)!.SqlState);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
