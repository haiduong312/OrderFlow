using Microsoft.Extensions.Options;

namespace OrderFlow.Worker;

public sealed class OrderExpiryWorker(OrderExpiryIteration iteration, IOptions<OrderExpiryOptions> options,
    TimeProvider clock, ILogger<OrderExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await iteration.ProcessAsync(stoppingToken);
                if (result.Expired > 0)
                    logger.LogInformation(new EventId(1400, "OrdersExpired"), "Expired {OrderCount} orders", result.Expired);
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        // Unexpected failures escape to Generic Host: log and stop, rather than report success.
    }
}
