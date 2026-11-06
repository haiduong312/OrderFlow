using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Application.Modules.Payments.Simulation;

public sealed record SimulatorSubmission(GatewayPaymentResult? Result, bool LoseResponse);

public sealed class SimulatorService(OrderFlowDbContext database, TimeProvider clock)
{
    private Task LockAsync(string key, CancellationToken cancellationToken) => database.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

    public static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 80 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Invalid provider key.");
    }

    public async Task<SimulatorSubmission> SubmitAsync(GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        ValidateKey(request.RequestKey);
        if (request.AttemptId == Guid.Empty || request.OrderId == Guid.Empty || request.Currency != "USD"
            || !UsdMoney.TryParse(request.Amount, out var amount) || amount.Value <= 0) throw new ArgumentException("Invalid payment request.");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        await LockAsync("provider-attempt/" + request.AttemptId.ToString("N"), cancellationToken);
        await LockAsync("provider-payment/" + request.RequestKey, cancellationToken);
        var existing = await database.ProviderPayments.SingleOrDefaultAsync(row => row.RequestKey == request.RequestKey, cancellationToken);
        if (existing is not null)
        {
            if (!existing.Matches(request, amount)) throw new SimulatorConflictException();
            return new(existing.View(), false);
        }
        if (await database.ProviderPayments.AnyAsync(row => row.AttemptId == request.AttemptId, cancellationToken))
            throw new SimulatorConflictException();
        var scenario = await database.ProviderScenarios.Where(row => row.RequestKey == request.RequestKey)
            .Select(row => (SimulatorScenario?)row.Scenario).SingleOrDefaultAsync(cancellationToken) ?? SimulatorScenario.Pending;
        if (scenario == SimulatorScenario.TimeoutBeforeAccept) return new(null, true);
        var payment = ProviderPayment.Create(request, amount, scenario, BusinessTime.GetUtcNow(clock));
        database.ProviderPayments.Add(payment);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // The host loses the response AFTER this independent provider commit.
        return new(payment.View(), scenario == SimulatorScenario.TimeoutAfterCapture);
    }

    public Task<ProviderPayment?> LookupAsync(string key, CancellationToken cancellationToken) =>
        database.ProviderPayments.AsNoTracking().SingleOrDefaultAsync(row => row.RequestKey == key, cancellationToken);

    public async Task SetScenarioAsync(string key, SimulatorScenario scenario, CancellationToken cancellationToken)
    {
        ValidateKey(key); if (!Enum.IsDefined(scenario)) throw new ArgumentException("Invalid scenario.");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        await LockAsync("provider-payment/" + key, cancellationToken);
        if (await database.ProviderPayments.AnyAsync(row => row.RequestKey == key, cancellationToken)) throw new SimulatorConflictException();
        var control = await database.ProviderScenarios.SingleOrDefaultAsync(row => row.RequestKey == key, cancellationToken);
        if (control is null) database.ProviderScenarios.Add(new() { RequestKey = key, Scenario = scenario });
        else control.Scenario = scenario;
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> ResolveAsync(string key, GatewayPaymentState state, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        await LockAsync("provider-payment/" + key, cancellationToken);
        var payment = await database.ProviderPayments.SingleOrDefaultAsync(row => row.RequestKey == key, cancellationToken);
        if (payment is null) return false;
        payment.Resolve(state, BusinessTime.GetUtcNow(clock));
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }

    public async Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken)
    {
        ValidateKey(request.RequestKey);
        if (string.IsNullOrEmpty(request.CaptureId) || request.CaptureId.Length > 100 || request.Currency != "USD"
            || !UsdMoney.TryParse(request.Amount, out var amount) || amount.Value <= 0) throw new ArgumentException("Invalid refund.");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        await LockAsync("provider-refund-capture/" + request.CaptureId, cancellationToken);
        await LockAsync("provider-refund-key/" + request.RequestKey, cancellationToken);
        var existing = await database.ProviderRefunds.SingleOrDefaultAsync(row => row.RequestKey == request.RequestKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.CaptureId != request.CaptureId || existing.Amount != amount || existing.Currency != request.Currency) throw new SimulatorConflictException();
            return existing.View();
        }
        var payment = await database.ProviderPayments.AsNoTracking().SingleOrDefaultAsync(row => row.CaptureId == request.CaptureId, cancellationToken);
        if (payment is null || payment.Amount != amount || payment.State != GatewayPaymentState.Succeeded
            || await database.ProviderRefunds.AnyAsync(row => row.CaptureId == request.CaptureId, cancellationToken)) throw new SimulatorConflictException();
        var refund = new ProviderRefund { RequestKey = request.RequestKey, RefundId = "ref_" + Guid.NewGuid().ToString("N"),
            PaymentRequestKey = payment.RequestKey, CaptureId = request.CaptureId, Amount = amount, CreatedAt = BusinessTime.GetUtcNow(clock) };
        database.ProviderRefunds.Add(refund); await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken); return refund.View();
    }

    public Task<ProviderRefund?> LookupRefundAsync(string key, CancellationToken cancellationToken) =>
        database.ProviderRefunds.AsNoTracking().SingleOrDefaultAsync(row => row.RequestKey == key, cancellationToken);
}
