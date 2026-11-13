using OrderFlow.Application.Messaging;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Api.Realtime;

public sealed class RealtimeConsumerWorker(RabbitMqSettings broker, RealtimeSettings settings, RealtimeDeliveryHandler handler,
    TimeProvider clock, ILogger<RealtimeConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try { await Task.WhenAll(RunAsync(broker.RealtimeQueue, stop), RunAsync(broker.RealtimeRetryQueue, stop)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    private async Task RunAsync(string queue, CancellationTokenSource stop)
    {
        try
        {
            int[] delays = [1, 5, 15, 30, 60, 120, 300];
            for (var attempt = 0; ; attempt++)
            {
                stop.Token.ThrowIfCancellationRequested();
                try
                {
                    await using var subscription = await RabbitMqSubscription.StartAsync(broker, settings.Prefetch, queue, handler.HandleAsync, stop.Token);
                    await subscription.Completion.WaitAsync(stop.Token);
                }
                catch (Exception failure) when (!stop.IsCancellationRequested && Retryable(failure) && attempt < 7)
                {
                    logger.LogWarning(new EventId(2404, "RealtimeBrokerReconnect"), "API {Instance} broker reconnect attempt {Attempt}", settings.InstanceName, attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(delays[attempt]), clock, stop.Token);
                }
            }
        }
        catch { stop.Cancel(); throw; }
    }
    private static bool Retryable(Exception error) => error switch
    {
        BrokerUnreachableException e => e.InnerException is not AuthenticationFailureException
            && !(e.InnerException is AggregateException a && a.Flatten().InnerExceptions.Any(x => x is AuthenticationFailureException)),
        OperationInterruptedException e => e.ShutdownReason?.ReplyCode is not (403 or 406),
        IOException or TimeoutException or OperationCanceledException => true,
        _ => false
    };
}
