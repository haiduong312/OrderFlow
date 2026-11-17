using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Observability;

public sealed record OperationalSnapshot(DateTimeOffset ObservedAt, long OutboxPending, double OutboxOldestAge,
    long OutboxFailed, long ExpiryDue, double ExpiryOldestLag, long RefundsPending, long RefundsReview);

// No database/network work occurs on an SDK collection/export callback. Sampling owns its own scope.
public sealed class OperationalMetrics : IDisposable
{
    private readonly Meter meter = new(OrderFlowTelemetry.MeterName);
    private readonly TimeProvider clock;
    private OperationalSnapshot? snapshot;
    private ReadinessReport? readiness;
    private int success;
    public OperationalSnapshot? Snapshot => Volatile.Read(ref snapshot);
    public OperationalMetrics(TimeProvider clock)
    {
        this.clock = clock;
        Gauge("orderflow_outbox_pending", s => s.OutboxPending);
        Gauge("orderflow_outbox_oldest_age", s => s.OutboxOldestAge, "s");
        Gauge("orderflow_outbox_failed", s => s.OutboxFailed);
        Gauge("orderflow_expiry_due", s => s.ExpiryDue);
        Gauge("orderflow_expiry_oldest_lag", s => s.ExpiryOldestLag, "s");
        Gauge("orderflow_refunds_pending", s => s.RefundsPending);
        Gauge("orderflow_refunds_review", s => s.RefundsReview);
        Gauge("orderflow_telemetry_snapshot_age", s => Math.Max(0, (clock.GetUtcNow() - s.ObservedAt).TotalSeconds), "s");
        meter.CreateObservableGauge("orderflow_telemetry_sample_success", () => Volatile.Read(ref success));
        meter.CreateObservableGauge("orderflow_dependency_ready", () =>
            Volatile.Read(ref readiness)?.Dependencies.Select(p => new Measurement<int>(p.Value ? 1 : 0,
                new KeyValuePair<string, object?>("dependency", p.Key))) ?? []);
        meter.CreateObservableGauge("orderflow_runtime_ready", () =>
            Volatile.Read(ref readiness) is { } value ? [new Measurement<int>(value.Ready ? 1 : 0)] : Array.Empty<Measurement<int>>());
    }
    private void Gauge(string name, Func<OperationalSnapshot, double> value, string? unit = null) =>
        meter.CreateObservableGauge(name, () => Snapshot is { } s ? [new Measurement<double>(value(s))] : Array.Empty<Measurement<double>>(), unit);
    public void SetReadiness(ReadinessReport value) => Volatile.Write(ref readiness, value);
    public void Failed() => Volatile.Write(ref success, 0);
    public void Update(OperationalSnapshot value)
    { Volatile.Write(ref snapshot, value); Volatile.Write(ref success, 1); }
    public void Dispose() => meter.Dispose();

    public async Task SampleAsync(IServiceScopeFactory scopes, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(5));
        using var suppress = SuppressInstrumentationScope.Begin();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
        db.Database.SetCommandTimeout(2);
        var now = BusinessTime.GetUtcNow(clock);
        var outbox = await db.OutboxMessages.AsNoTracking().Where(m => m.Status != OutboxStatus.Published).GroupBy(_ => 1).Select(g => new
        {
            Pending = g.LongCount(m => m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.InFlight),
            Oldest = g.Where(m => m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.InFlight).Min(m => (DateTimeOffset?)m.OccurredAt),
            Failed = g.LongCount(m => m.Status == OutboxStatus.Failed)
        }).SingleOrDefaultAsync(budget.Token);
        var due = await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.PendingPayment && o.ExpiresAt <= now)
            .GroupBy(_ => 1).Select(g => new { Count = g.LongCount(), Oldest = g.Min(o => o.ExpiresAt) }).SingleOrDefaultAsync(budget.Token);
        var refunds = await db.PaymentRefunds.AsNoTracking().Where(r => r.Status != PaymentRefundStatus.Succeeded).GroupBy(_ => 1).Select(g => new
        {
            Pending = g.LongCount(r => r.Status == PaymentRefundStatus.Pending),
            Review = g.LongCount(r => r.Status == PaymentRefundStatus.NeedsReview)
        }).SingleOrDefaultAsync(budget.Token);
        Update(new(now, outbox?.Pending ?? 0, Age(now, outbox?.Oldest), outbox?.Failed ?? 0,
            due?.Count ?? 0, Age(now, due?.Oldest), refunds?.Pending ?? 0, refunds?.Review ?? 0));
    }
    private static double Age(DateTimeOffset now, DateTimeOffset? time) => time is { } value ? Math.Max(0, (now - value).TotalSeconds) : 0;
}

public sealed class OperationalMetricsWorker(IServiceScopeFactory scopes, OperationalMetrics metrics, IRuntimeReadiness readiness,
    TelemetrySettings settings, TimeProvider clock, ILogger<OperationalMetricsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                metrics.SetReadiness(await readiness.CheckAsync(stoppingToken));
                await metrics.SampleAsync(scopes, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is NpgsqlException or TimeoutException or InvalidOperationException or OperationCanceledException)
            {
                metrics.Failed();
                logger.LogWarning(new EventId(2801, "OperationalSampleFailed"),
                    "Operational sampling unavailable with {ExceptionType}; previous values retained with age and success=0", error.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.SnapshotIntervalSeconds), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
