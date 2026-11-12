using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Worker.Messaging;

public sealed class OutboxPublisherWorker(OutboxPublishingIteration iteration, TimeProvider clock,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
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
                    logger.LogWarning(new EventId(1803, "OutboxDatabaseContention"),
                        "Outbox pass postponed with PostgreSQL state {SqlState}", OrderDatabase.Contention(failure)!.SqlState);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
