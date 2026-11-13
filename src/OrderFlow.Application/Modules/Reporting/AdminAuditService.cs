using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Reporting;

public sealed class AdminAuditService(OrderFlowDbContext database, TimeProvider clock)
{
    public async Task<AuditPage<OrderStatusHistoryResponse>> HistoryAsync(Guid orderId, AuditQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request); var page = PageRequest.Create(request.Page, request.PageSize);
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        await RequireOrderAsync(orderId, token);
        var query = database.OrderStatusHistory.AsNoTracking().Where(row => row.OrderId == orderId
            && row.OccurredAt >= range.Start && row.OccurredAt < range.End);
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Version)
            .Skip(page.Offset).Take(page.PageSize).Select(row => new OrderStatusHistoryResponse(row.Version,
                row.FromStatus == null ? null : row.FromStatus.ToString(), row.Status.ToString(), row.ActorKind.ToString(),
                row.ActorUserId, row.OccurredAt, row.Reason)).ToListAsync(token);
        return new(rows, page.Page, page.PageSize, count, snapshot.ObservedAt);
    }

    public async Task<AuditPage<PaymentAudit>> PaymentsAsync(Guid orderId, AuditQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request); var page = PageRequest.Create(request.Page, request.PageSize);
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        await RequireOrderAsync(orderId, token);
        var query = database.PaymentAttempts.AsNoTracking().Where(row => row.OrderId == orderId
            && row.CreatedAt >= range.Start && row.CreatedAt < range.End);
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(page.Offset).Take(page.PageSize).Select(row => new { row.Id, row.OrderId, row.Status, row.Amount,
                row.CreatedAt, row.UpdatedAt, row.NeedsAttention }).ToListAsync(token);
        return new(rows.Select(row => new PaymentAudit(row.Id, row.OrderId, row.Status.ToString(), row.Amount.ToString(),
            row.CreatedAt, row.UpdatedAt, row.NeedsAttention)).ToArray(), page.Page, page.PageSize, count, snapshot.ObservedAt);
    }

    public async Task<AuditPage<CaptureAudit>> CapturesAsync(Guid orderId, AuditQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request); var page = PageRequest.Create(request.Page, request.PageSize);
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        await RequireOrderAsync(orderId, token);
        var query = database.PaymentCaptures.AsNoTracking().Where(row => row.OrderId == orderId
            && row.RecordedAt >= range.Start && row.RecordedAt < range.End);
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id)
            .Skip(page.Offset).Take(page.PageSize).Select(row => new { row.Id, row.OrderId, row.AttemptId, row.Amount,
                row.Allocated, row.RecordedAt, row.ProviderOccurredAt }).ToListAsync(token);
        return new(rows.Select(row => new CaptureAudit(row.Id, row.OrderId, row.AttemptId, row.Amount.ToString(), row.Allocated,
            row.RecordedAt, row.ProviderOccurredAt)).ToArray(), page.Page, page.PageSize, count, snapshot.ObservedAt);
    }

    public async Task<AuditPage<RefundAudit>> RefundsAsync(Guid orderId, AuditQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request); var page = PageRequest.Create(request.Page, request.PageSize);
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        await RequireOrderAsync(orderId, token);
        var query = from refund in database.PaymentRefunds.AsNoTracking()
                    join capture in database.PaymentCaptures.AsNoTracking() on refund.CaptureId equals capture.Id
                    where capture.OrderId == orderId && refund.CreatedAt >= range.Start && refund.CreatedAt < range.End
                    select new { refund.Id, refund.CaptureId, capture.OrderId, refund.Status, refund.Amount,
                        refund.CreatedAt, refund.UpdatedAt, refund.SucceededAt };
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip(page.Offset).Take(page.PageSize).ToListAsync(token);
        return new(rows.Select(row => new RefundAudit(row.Id, row.CaptureId, row.OrderId, row.Status.ToString(), row.Amount.ToString(),
            row.CreatedAt, row.UpdatedAt, row.SucceededAt)).ToArray(), page.Page, page.PageSize, count, snapshot.ObservedAt);
    }

    public async Task<AuditPage<StockMovementResponse>> StockAsync(StockAuditQuery request, CancellationToken token)
    {
        var range = ReportRange.Create(request); var page = PageRequest.Create(request.Page, request.PageSize);
        if (request.OrderId == Guid.Empty || request.ProductId == Guid.Empty)
            throw ReportRange.Invalid("orderId", "Optional order/product IDs must be nonempty UUIDs.");
        await using var snapshot = await ReportSnapshot.OpenAsync(database, clock, token);
        var query = database.StockMovements.AsNoTracking().Where(row => row.OccurredAt >= range.Start && row.OccurredAt < range.End);
        if (request.OrderId is { } orderId) query = query.Where(row => row.OrderId == orderId);
        if (request.ProductId is { } productId) query = query.Where(row => row.ProductId == productId);
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
            .Skip(page.Offset).Take(page.PageSize).Select(row => new StockMovementResponse(row.Id, row.ProductId, row.Operation,
                row.OperationId, row.Kind, row.ActorKind, row.ActorUserId, row.Reason, row.OnHandDelta, row.ReservedDelta,
                row.OnHandBefore, row.ReservedBefore, row.OnHandAfter, row.ReservedAfter, row.Revision, row.OccurredAt,
                row.OrderId, row.ReservationId)).ToListAsync(token);
        return new(rows, page.Page, page.PageSize, count, snapshot.ObservedAt);
    }

    private async Task RequireOrderAsync(Guid id, CancellationToken token)
    {
        if (!await database.Orders.AsNoTracking().AnyAsync(order => order.Id == id, token))
            throw new ApplicationFailureException(ApplicationError.NotFound);
    }
}
