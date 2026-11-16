using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Payments;

public sealed record CustomerCapture(Guid Id, Guid AttemptId, string Amount, string Currency, bool Allocated, DateTimeOffset RecordedAt);
public sealed record CustomerRefund(Guid Id, Guid CaptureId, string Amount, string Currency, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? SucceededAt);
public sealed record CustomerPaymentStatusResponse(Guid OrderId, DateTimeOffset ObservedAt, int Page, int PageSize,
    IReadOnlyList<PaymentAttemptResponse> Attempts, int AttemptCount,
    IReadOnlyList<CustomerCapture> Captures, int CaptureCount, IReadOnlyList<CustomerRefund> Refunds, int RefundCount);

// Owner-only read model. No provider keys/IDs, raw observations, leases or control credentials leave this boundary.
public sealed class CustomerPaymentStatus(OrderFlowDbContext database, ICurrentUser user, TimeProvider clock)
{
    public async Task<CustomerPaymentStatusResponse> ReadAsync(Guid orderId, int page, int pageSize, CancellationToken token)
    {
        var owner = user.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        var paging = PageRequest.Create(page, pageSize);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        await OrderDatabase.SetTimeoutsAsync(database, token);
        if (!await database.Orders.AnyAsync(order => order.Id == orderId && order.UserId == owner, token))
            throw new ApplicationFailureException(ApplicationError.NotFound);
        var attempts = database.PaymentAttempts.AsNoTracking().Where(row => row.OrderId == orderId);
        var captures = database.PaymentCaptures.AsNoTracking().Where(row => row.OrderId == orderId);
        var refunds = database.PaymentRefunds.AsNoTracking().Where(row => captures.Any(capture => capture.Id == row.CaptureId));
        var attemptCount = await attempts.CountAsync(token);
        var captureCount = await captures.CountAsync(token);
        var refundCount = await refunds.CountAsync(token);
        var attemptRows = await attempts.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(paging.Offset).Take(paging.PageSize).Select(row => new { row.Id, row.OrderId, row.Status, row.Amount, row.Currency,
                row.CreatedAt, row.UpdatedAt, row.ObservedState, row.NeedsAttention, row.LastErrorCode }).ToListAsync(token);
        var captureRows = await captures.OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id)
            .Skip(paging.Offset).Take(paging.PageSize).Select(row => new { row.Id, row.AttemptId, row.Amount, row.Currency, row.Allocated, row.RecordedAt }).ToListAsync(token);
        var refundRows = await refunds.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(paging.Offset).Take(paging.PageSize).Select(row => new { row.Id, row.CaptureId, row.Amount, row.Currency, row.Status, row.CreatedAt, row.UpdatedAt, row.SucceededAt }).ToListAsync(token);
        var result = new CustomerPaymentStatusResponse(orderId, BusinessTime.GetUtcNow(clock), page, pageSize,
            attemptRows.Select(row => new PaymentAttemptResponse(row.Id, row.OrderId, row.Status.ToString(), row.Amount.ToString(), row.Currency,
                row.CreatedAt, row.UpdatedAt, row.ObservedState?.ToString(), row.NeedsAttention, row.LastErrorCode)).ToArray(), attemptCount,
            captureRows.Select(row => new CustomerCapture(row.Id, row.AttemptId, row.Amount.ToString(), row.Currency, row.Allocated, row.RecordedAt)).ToArray(), captureCount,
            refundRows.Select(row => new CustomerRefund(row.Id, row.CaptureId, row.Amount.ToString(), row.Currency, row.Status.ToString(), row.CreatedAt, row.UpdatedAt, row.SucceededAt)).ToArray(), refundCount);
        await transaction.CommitAsync(token);
        return result;
    }
}
