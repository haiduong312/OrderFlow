using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

public sealed class InitiatePaymentRequest;
public sealed record PaymentAttemptResponse(Guid Id, Guid OrderId, string Status, string Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? ObservedProviderState, bool NeedsAttention, string? LastErrorCode);

public sealed class PaymentInitiationService(OrderFlowDbContext database, ICurrentUser user, TimeProvider clock,
    ILogger<PaymentInitiationService> logger)
{
    public async Task<CheckoutResponse> InitiateAsync(Guid orderId, string? key, CancellationToken cancellationToken)
    {
        var owner = user.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        var validatedKey = CheckoutRequest.ValidateKey(key);
        try { return await InitiateLockedAsync(orderId, owner, validatedKey, cancellationToken); }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
        {
            logger.LogWarning(new EventId(1501, "PaymentInitiationContention"), "Payment initiation rolled back with PostgreSQL state {SqlState}", OrderDatabase.Contention(failure)!.SqlState);
            throw new ApplicationFailureException(ApplicationError.ServiceUnavailable);
        }
    }

    private async Task<CheckoutResponse> InitiateLockedAsync(Guid orderId, Guid owner, string key, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var order = await OrderLocking.LockAsync(database, orderId, owner, cancellationToken);
        var operation = "StartPayment:v1/" + orderId.ToString("D");
        var prior = await database.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == owner
            && row.Operation == operation && row.Key == key, cancellationToken);
        if (prior is not null)
        {
            if (prior.Status != IdempotencyStatus.Completed || prior.CanonicalRequest != "{\"version\":1}")
                throw new InvalidOperationException("The payment request has no valid committed snapshot.");
            return new(202, prior.ResponseContentType!, prior.ResponseLocation!, prior.ResponseBody!);
        }
        var now = BusinessTime.GetUtcNow(clock);
        if (order.Status == OrderStatus.Expired) throw new ApplicationFailureException(ApplicationError.OrderExpired);
        if (order.Expire(now) is { } expired)
        {
            // If due, finalize expiry/release in this transaction before returning the expired result.
            await OrderReservationRelease.ReleaseAsync(database, order, null, now, cancellationToken);
            database.OrderStatusHistory.Add(expired);
            database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-expired", now, Guid.NewGuid()));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new ApplicationFailureException(ApplicationError.OrderExpired);
        }
        if (order.Status != OrderStatus.PendingPayment) throw new ApplicationFailureException(ApplicationError.StateConflict);
        var attempt = await database.PaymentAttempts.SingleOrDefaultAsync(row => row.OrderId == orderId && row.Status != PaymentAttemptStatus.Failed, cancellationToken);
        if (attempt is null) { attempt = PaymentAttempt.Create(order, now); database.PaymentAttempts.Add(attempt); }
        var body = JsonSerializer.SerializeToUtf8Bytes(View(attempt), GatewayJson.Options);
        var location = $"/api/orders/{orderId:D}/payment-attempts/{attempt.Id:D}";
        var claim = IdempotencyRecord.Create(owner, operation, key, "{\"version\":1}", now);
        claim.Complete(202, "application/json; charset=utf-8", body, location, now);
        database.IdempotencyRecords.Add(claim);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(202, claim.ResponseContentType!, location, body);
    }

    public async Task<PaymentAttemptResponse> GetAsync(Guid orderId, Guid attemptId, CancellationToken cancellationToken)
    {
        var owner = user.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        var attempt = await database.PaymentAttempts.AsNoTracking().Where(row => row.Id == attemptId && row.OrderId == orderId
            && database.Orders.Any(order => order.Id == row.OrderId && order.UserId == owner)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        return View(attempt);
    }

    public static PaymentAttemptResponse View(PaymentAttempt attempt) => new(attempt.Id, attempt.OrderId, attempt.Status.ToString(),
        attempt.Amount.ToString(), attempt.Currency, attempt.CreatedAt, attempt.UpdatedAt, attempt.ObservedState?.ToString(), attempt.NeedsAttention, attempt.LastErrorCode);
}
