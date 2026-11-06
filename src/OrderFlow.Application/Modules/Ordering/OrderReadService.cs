using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

public sealed class OrderReadService(OrderFlowDbContext database, ICurrentUser currentUser, TimeProvider clock)
{
    public Task<OrderPage> CustomerListAsync(CustomerOrderQuery query, CancellationToken cancellationToken) =>
        ListAsync(query.Page, query.PageSize, query.Status, UserId(), customerView: true, cancellationToken);

    public Task<OrderPage> AdminListAsync(AdminOrderQuery query, CancellationToken cancellationToken)
    {
        if (query.CustomerId == Guid.Empty) throw CheckoutRequest.Invalid("customerId", "Customer ID must be nonempty.");
        return ListAsync(query.Page, query.PageSize, query.Status, query.CustomerId, customerView: false, cancellationToken);
    }

    public Task<OrderDetailResponse> CustomerDetailAsync(Guid id, CancellationToken cancellationToken) =>
        DetailAsync(id, UserId(), customerView: true, cancellationToken);
    public Task<OrderDetailResponse> AdminDetailAsync(Guid id, CancellationToken cancellationToken) =>
        DetailAsync(id, null, customerView: false, cancellationToken);

    private async Task<OrderPage> ListAsync(int pageNumber, int pageSize, string? status, Guid? owner,
        bool customerView, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(pageNumber, pageSize);
        var filter = status is null ? (OrderStatus?)null : OrderTransitions.ParseStatus(status);
        // A small read-only snapshot keeps count/page consistent without locking orders or expiring them.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var query = database.Orders.AsNoTracking();
        if (owner is { } userId) query = query.Where(order => order.UserId == userId);
        if (filter is { } value) query = query.Where(order => order.Status == value);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Skip(page.Offset).Take(page.PageSize).ToListAsync(cancellationToken);
        var now = BusinessTime.GetUtcNow(clock);
        var response = new OrderPage(rows.Select(order => new OrderSummaryResponse(order.Id, order.UserId,
            order.Status.ToString(), order.Version, order.CreatedAt, order.ExpiresAt, order.Total.ToString(),
            OrderViews.Expiry(order, now, customerView))).ToArray(), page.Page, page.PageSize, count);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private async Task<OrderDetailResponse> DetailAsync(Guid id, Guid? owner, bool customerView, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var query = database.Orders.AsNoTracking().Where(order => order.Id == id);
        if (owner is { } userId) query = query.Where(order => order.UserId == userId);
        var order = await query.SingleOrDefaultAsync(cancellationToken) ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        var items = await database.OrderItems.AsNoTracking().Where(item => item.OrderId == id).ToListAsync(cancellationToken);
        var history = await database.OrderStatusHistory.AsNoTracking().Where(entry => entry.OrderId == id)
            .OrderBy(entry => entry.Version).ToListAsync(cancellationToken);
        var result = OrderViews.Detail(order, items, history, BusinessTime.GetUtcNow(clock), customerView);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private Guid UserId() => currentUser.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
}
