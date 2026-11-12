using RabbitMQ.Client;

namespace OrderFlow.Worker.Messaging;

public sealed class RabbitMqSettings
{
    public string HostName { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "orderflow";
    public string UserName { get; init; } = "orderflow_dev";
    public string Password { get; init; } = "";
    public string Prefix { get; init; } = "orderflow";
    public int PublishTimeoutSeconds { get; init; } = 15;
    public int RetryDelayMilliseconds { get; init; } = 5000;
    public int QueueMaxLength { get; init; } = 10000;
    public string Exchange => Prefix + ".events.v1";
    public string Queue => Prefix + ".integration.v1";
    public string RetryExchange => Prefix + ".retry.v1";
    public string DelayQueue => Prefix + ".integration.delay.v1";
    public string RetryReadyExchange => Prefix + ".retry-ready.v1";
    public string RetryQueue => Prefix + ".integration.retry.v1";
    public string DeadExchange => Prefix + ".dead.v1";
    public string DeadQueue => Prefix + ".integration.dead.v1";

    public string NotificationQueue => Prefix + ".notifications.orders.v1";
    public string NotificationDelayQueue => Prefix + ".notifications.orders.delay.v1";
    public string NotificationRetryQueue => Prefix + ".notifications.orders.retry.v1";
    public string NotificationDeadQueue => Prefix + ".notifications.orders.dead.v1";
    public string NotificationRetryExchange => Prefix + ".notifications.retry.v1";
    public string NotificationReadyExchange => Prefix + ".notifications.retry-ready.v1";
    public string NotificationDeadExchange => Prefix + ".notifications.dead.v1";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HostName) || Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(VirtualHost)
            || string.IsNullOrWhiteSpace(UserName) || Password.Length is < 16 or > 512
            || Prefix.Length is < 1 or > 80 || Prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
            || PublishTimeoutSeconds is < 1 or > 30 || RetryDelayMilliseconds is < 100 or > 300000
            || QueueMaxLength is < 1 or > 1000000)
            throw new InvalidOperationException("Configure valid RabbitMq endpoint, private password, topology prefix and bounded publish/retry limits.");
    }

    public ConnectionFactory Factory() => new()
    {
        HostName = HostName, Port = Port, VirtualHost = VirtualHost, UserName = UserName, Password = Password,
        AutomaticRecoveryEnabled = false, TopologyRecoveryEnabled = false,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5), ContinuationTimeout = TimeSpan.FromSeconds(5),
        RequestedHeartbeat = TimeSpan.FromSeconds(30)
    };
}
