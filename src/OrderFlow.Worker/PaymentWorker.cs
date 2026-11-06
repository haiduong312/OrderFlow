using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Worker;

public sealed class PaymentWorker(PaymentProcessingIteration iteration, TimeProvider clock, ILogger<PaymentWorker> logger) : BackgroundService
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
                    logger.LogWarning(new EventId(1503, "PaymentProcessingContention"), "Payment processing postponed with PostgreSQL state {SqlState}", OrderDatabase.Contention(failure)!.SqlState);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
