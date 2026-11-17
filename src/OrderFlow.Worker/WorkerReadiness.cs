using System.Net.Sockets;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Observability;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Worker;

// Internal readiness (CLI + telemetry), separate from process liveness and API admission.
public sealed class WorkerReadiness(DatabaseReadiness database, IConfiguration configuration, IServiceProvider services,
    ILogger<WorkerReadiness> logger) : IRuntimeReadiness
{
    public async Task<ReadinessReport> CheckAsync(CancellationToken token)
    {
        var dependencies = new Dictionary<string, bool> { ["postgresql"] = await database.CheckAsync(token) };
        if (configuration.GetValue("OutboxPublishing:Enabled", false) || configuration.GetValue("Notifications:Enabled", false))
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await using var connection = await services.GetRequiredService<RabbitMqSettings>().Factory().CreateConnectionAsync(budget.Token);
                dependencies["rabbitmq"] = connection.IsOpen;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { dependencies["rabbitmq"] = false; }
            catch (Exception error) when (error is BrokerUnreachableException or IOException or TimeoutException)
            { dependencies["rabbitmq"] = false; }
        }
        if (configuration.GetValue("EmailSending:Enabled", false))
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(configuration["EmailSending:Host"] ?? "127.0.0.1", configuration.GetValue("EmailSending:Port", 1025), budget.Token);
                dependencies["smtp"] = client.Connected; // Reachability only, never a successful-delivery guarantee.
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { dependencies["smtp"] = false; }
            catch (SocketException) { dependencies["smtp"] = false; }
        }
        foreach (var pair in dependencies.Where(p => !p.Value))
            logger.LogWarning(new EventId(2802, "WorkerDependencyNotReady"), "Worker dependency {Dependency} is not ready", pair.Key);
        return new(dependencies.Values.All(value => value), dependencies);
    }
}
