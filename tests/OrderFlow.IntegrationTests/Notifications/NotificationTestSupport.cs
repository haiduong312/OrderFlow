using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace OrderFlow.IntegrationTests.Notifications;

internal sealed class NotificationInsertGate : DbCommandInterceptor
{
    private readonly RaceRendezvous rendezvous = new();
    public int Participants => rendezvous.Participants;
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
        InterceptionResult<int> result, CancellationToken token = default)
    {
        if (command.CommandText.StartsWith("INSERT INTO messaging.\"ProcessedMessages\"", StringComparison.Ordinal))
            await rendezvous.ArriveAsync(data.Context!.ContextId.InstanceId, token);
        return result;
    }
}

internal sealed class DeliveryLog<T>(int acknowledgements = 1) : ILogger<T>
{
    private int ackCount;
    public TaskCompletionSource Acknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public System.Collections.Concurrent.ConcurrentQueue<int> Events { get; } = new();
    public bool IsEnabled(LogLevel level) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    {
        Events.Enqueue(id.Id);
        if (id.Id == 1904 && Interlocked.Increment(ref ackCount) >= acknowledgements) Acknowledged.TrySetResult();
    }
}

// Delegate every protocol call to the actual channel except a deterministic fault/probe at ACK.
public class AckProbeChannel : DispatchProxy
{
    public IChannel Target { get; set; } = null!;
    public Func<Task> BeforeAck { get; set; } = null!;
    public static IChannel Wrap(IChannel target, Func<Task> beforeAck)
    {
        var result = Create<IChannel, AckProbeChannel>(); var proxy = (AckProbeChannel)result;
        proxy.Target = target; proxy.BeforeAck = beforeAck; return result;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IChannel.BasicAckAsync)) return new ValueTask(AckAsync(method, args));
        try { return method.Invoke(Target, args); }
        catch (TargetInvocationException failure) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException!).Throw(); throw; }
    }
    private async Task AckAsync(MethodInfo method, object?[]? args)
    {
        await BeforeAck(); await (ValueTask)method.Invoke(Target, args)!;
    }
}
