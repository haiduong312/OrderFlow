using Xunit;

namespace OrderFlow.IntegrationTests;

// These harness checks need neither PostgreSQL nor Docker. Domain unit tests remain in UnitTests.
public sealed class HarnessSynchronizationTests
{
    [Fact]
    public async Task Rendezvous_counts_distinct_contexts_and_never_waits_again_on_later_rows()
    {
        var gate = new RaceRendezvous();
        var firstContext = Guid.NewGuid();
        var first = gate.ArriveAsync(firstContext, default);
        Assert.False(first.IsCompleted);
        await gate.ArriveAsync(firstContext, default);
        Assert.Equal(1, gate.Participants);
        Assert.False(first.IsCompleted);
        await Task.WhenAll(first, gate.ArriveAsync(Guid.NewGuid(), default));
        Assert.Equal(2, gate.Participants);
        await gate.ArriveAsync(firstContext, default);
    }

    [Fact]
    public async Task Cancelled_participant_releases_peers_with_an_explicit_failure()
    {
        var gate = new RaceRendezvous(3);
        using var cancellation = new CancellationTokenSource();
        var first = gate.ArriveAsync(Guid.NewGuid(), cancellation.Token);
        var second = gate.ArriveAsync(Guid.NewGuid(), default);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Contains("2/3", failure.Message);
    }
}
