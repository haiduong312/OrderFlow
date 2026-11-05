using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OrderFlow.IntegrationTests;

/// <summary>
/// A one-shot rendezvous for distinct units of work. Install only BEFORE the first shared
/// lock, or at independent writes whose earlier locks are on disjoint rows. Never wait
/// for another request while holding a lock that request needs to reach this boundary.
/// </summary>
public sealed class RaceRendezvous(int expectedParticipants = 2)
{
    private readonly ConcurrentDictionary<Guid, byte> participants = new();
    private readonly TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Participants => participants.Count;

    public async Task ArriveAsync(Guid unitOfWork, CancellationToken cancellationToken)
    {
        // Multi-row operations rendezvous only on their first matched statement. A later row
        // must never re-enter a barrier after the context owns its first row lock.
        if (!participants.TryAdd(unitOfWork, 0)) return;
        if (Participants == expectedParticipants) arrived.TrySetResult();
        try
        {
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (Exception failure) when (failure is TimeoutException or OperationCanceledException)
        {
            // Release any peer with a diagnostic failure; no indefinitely stranded participant.
            arrived.TrySetException(new InvalidOperationException(
                $"Race rendezvous aborted: {Participants}/{expectedParticipants} distinct units of work arrived.", failure));
            throw;
        }
    }
}

public class CommandRaceGate(Func<string, bool> matches) : DbCommandInterceptor
{
    private readonly RaceRendezvous rendezvous = new();
    public bool Enabled { get; set; }
    public int Participants => rendezvous.Participants;

    protected virtual void Observe(DbCommand command, CommandEventData eventData) { }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && matches(command.CommandText))
        {
            Observe(command, eventData);
            await rendezvous.ArriveAsync(eventData.Context!.ContextId.InstanceId, cancellationToken);
        }
        return result;
    }
}
