using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Worker.Messaging;

public sealed class NotificationConsumerWorker(RabbitMqSettings settings, NotificationConsumerOptions options,
    NotificationDeliveryHandler handler, TimeProvider clock, ILogger<NotificationConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var tasks = new List<Task>();
        foreach (var queue in new[] { settings.NotificationQueue, settings.NotificationRetryQueue })
            for (var i = 0; i < options.ConsumersPerQueue; i++) tasks.Add(RunAsync(queue, stop));
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunAsync(string queue, CancellationTokenSource stop)
    {
        try
        {
            int[] delays = [1, 5, 15, 30, 60, 120, 300, 300];
            for (var attempt = 0; ; attempt++)
            {
                stop.Token.ThrowIfCancellationRequested();
                try
                {
                    await using var subscription = await NotificationConsumerSubscription.StartAsync(settings, options, queue, handler, stop.Token);
                    await subscription.Completion.WaitAsync(stop.Token);
                }
                catch (Exception failure) when (!stop.IsCancellationRequested && RetryableBrokerFailure(failure) && attempt < 7)
                {
                    logger.LogWarning(new EventId(1903, "NotificationConsumerReconnect"),
                        "Notification subscription reconnect attempt {Attempt} for queue {Queue}", attempt + 1, queue);
                    await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), clock, stop.Token);
                }
            }
        }
        catch { stop.Cancel(); throw; } // One failed slot stops siblings; unknown failures cannot disappear in callbacks.
    }

    private static bool RetryableBrokerFailure(Exception error) => error switch
    {
        BrokerUnreachableException failure => failure.InnerException is not AuthenticationFailureException
            && !(failure.InnerException is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(e => e is AuthenticationFailureException)),
        OperationInterruptedException failure => failure.ShutdownReason?.ReplyCode is not (403 or 406),
        IOException or TimeoutException => true,
        OperationCanceledException => true, // Only matched when host/session shutdown was not requested: startup budget expired.
        _ => false
    };
}
