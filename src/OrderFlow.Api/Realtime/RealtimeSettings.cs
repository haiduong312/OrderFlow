using StackExchange.Redis;

namespace OrderFlow.Api.Realtime;

public sealed class RealtimeSettings
{
    public bool Enabled { get; init; }
    public bool DispatchEnabled { get; init; } = true;
    public string RedisConnectionString { get; init; } = "";
    public string RedisPrefix { get; init; } = "orderflow.realtime.v1";
    public string InstanceName { get; init; } = "api-" + Guid.NewGuid().ToString("N");
    public ushort Prefetch { get; init; } = 4;
    public ConfigurationOptions RedisOptions()
    {
        if (string.IsNullOrWhiteSpace(RedisConnectionString) || RedisPrefix.Length is < 1 or > 80
            || RedisPrefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
            || InstanceName.Length is < 1 or > 80 || InstanceName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
            || Prefetch is < 1 or > 16)
            throw new InvalidOperationException("Configure private Realtime Redis endpoint, safe prefix/instance name and prefetch 1–16.");
        ConfigurationOptions options;
        try { options = ConfigurationOptions.Parse(RedisConnectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("Configure a valid private Realtime Redis connection string."); }
        options.AbortOnConnectFail = false; options.ConnectTimeout = 2000; options.AsyncTimeout = 2000; options.SyncTimeout = 2000;
        options.ConnectRetry = 1; options.BacklogPolicy = BacklogPolicy.FailFast;
        options.ChannelPrefix = RedisChannel.Literal(RedisPrefix);
        options.IncludeDetailInExceptions = false; options.IncludePerformanceCountersInExceptions = false;
        return options;
    }
}
