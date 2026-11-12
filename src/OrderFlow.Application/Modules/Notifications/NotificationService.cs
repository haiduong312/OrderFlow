using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Notifications;

public sealed class NotificationQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public bool UnreadOnly { get; init; }
}
public sealed record NotificationResponse(Guid Id, Guid SourceEventId, string Type, Guid OrderId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record UnreadNotificationCount(int Count);
public sealed record MarkNotificationReadRequest;

public sealed class NotificationService(OrderFlowDbContext database, ICurrentUser currentUser, TimeProvider clock)
{
    private Guid UserId() => currentUser.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
    private IQueryable<Notification> Own() { var id = UserId(); return database.Notifications.AsNoTracking().Where(n => n.RecipientUserId == id); }
    private static NotificationResponse View(Notification n) => new(n.Id, n.SourceEventId, n.Type, n.OrderId, n.CreatedAt, n.ReadAt);
    public async Task<NotificationPage> ListAsync(NotificationQuery query, CancellationToken token)
    {
        var page = PageRequest.Create(query.Page, query.PageSize);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        var rows = Own(); if (query.UnreadOnly) rows = rows.Where(n => n.ReadAt == null);
        var total = await rows.CountAsync(token);
        var items = await rows.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(page.Offset).Take(page.PageSize).ToListAsync(token);
        await transaction.CommitAsync(token);
        return new(items.Select(View).ToArray(), page.Page, page.PageSize, total);
    }
    public async Task<UnreadNotificationCount> UnreadAsync(CancellationToken token) => new(await Own().CountAsync(n => n.ReadAt == null, token));
    public async Task<NotificationResponse> GetAsync(Guid id, CancellationToken token) =>
        View(await Own().SingleOrDefaultAsync(n => n.Id == id, token) ?? throw new ApplicationFailureException(ApplicationError.NotFound));
    public async Task<NotificationResponse> ReadAsync(Guid id, CancellationToken token)
    {
        var now = BusinessTime.GetUtcNow(clock);
        await Own().Where(n => n.Id == id && n.ReadAt == null).ExecuteUpdateAsync(update => update
            .SetProperty(n => n.ReadAt, n => n.CreatedAt > now ? n.CreatedAt : now), token);
        return await GetAsync(id, token); // Same scoped identity even for no-op/unknown/non-owned IDs.
    }
}
