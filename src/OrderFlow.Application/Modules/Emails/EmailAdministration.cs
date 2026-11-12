using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Emails;

public sealed record EmailRetryRequest(Guid OperationId, string Reason, bool AcknowledgeDuplicateRisk = false);
public sealed record EmailDeliveryMetadata(Guid Id, Guid SourceEventId, Guid OrderId, string Template, string Provider, string ProviderKey,
    string Status, int AttemptCount, int AttemptsInCycle, int ReviewRetryCount, DateTimeOffset NextAttemptAt, DateTimeOffset? LastFailureAt,
    string? LastErrorCode, bool OutcomeUncertain, DateTimeOffset? SentAt);
public sealed record EmailDeliveryPage(IReadOnlyList<EmailDeliveryMetadata> Items, int Page, int PageSize, int TotalCount);
public sealed class EmailAdministration(OrderFlowDbContext db, ICurrentUser user, TimeProvider clock)
{
    private static EmailDeliveryMetadata View(EmailDelivery d) => new(d.Id, d.SourceEventId, d.OrderId, d.Template, d.Provider, d.ProviderKey,
        d.Status.ToString(), d.AttemptCount, d.AttemptsInCycle, d.ReviewRetryCount, d.NextAttemptAt, d.LastFailureAt, d.LastErrorCode, d.OutcomeUncertain, d.SentAt);
    public async Task<EmailDeliveryPage> FailuresAsync(int page, int pageSize, CancellationToken token)
    {
        var pagination = PageRequest.Create(page, pageSize);
        var query = db.EmailDeliveries.AsNoTracking().Where(d => d.Status == EmailDeliveryStatus.NeedsReview || d.LastErrorCode != null);
        var count = await query.CountAsync(token);
        var rows = await query.OrderByDescending(d => d.UpdatedAt).ThenByDescending(d => d.Id).Skip(pagination.Offset).Take(pagination.PageSize).ToListAsync(token);
        return new(rows.Select(View).ToArray(), pagination.Page, pagination.PageSize, count);
    }
    public async Task<EmailDeliveryMetadata> RetryAsync(Guid id, EmailRetryRequest request, CancellationToken token)
    {
        var actor = user.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        var reason = request.Reason?.Trim();
        if (request.OperationId == Guid.Empty || reason is null || reason.Length is < 1 or > 300 || reason.Any(char.IsControl))
            throw new ApplicationFailureException(ApplicationError.ValidationFailed);
        await using var transaction = await db.Database.BeginTransactionAsync(token); await OrderDatabase.SetTimeoutsAsync(db, token);
        var now = BusinessTime.GetUtcNow(clock);
        var row = await db.EmailDeliveries.FromSqlInterpolated($"SELECT * FROM notifications.\"EmailDeliveries\" WHERE \"Id\"={id} FOR UPDATE")
            .SingleOrDefaultAsync(token) ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications."EmailDeliveryRetries" ("OperationId","DeliveryId","ActorUserId","CycleNumber","Reason","AcknowledgeDuplicateRisk","RequestedAt")
            VALUES ({request.OperationId},{id},{actor},{row.ReviewRetryCount + 1},{reason},{request.AcknowledgeDuplicateRisk},{now}) ON CONFLICT ("OperationId") DO NOTHING
            """, token);
        var audit = await db.EmailDeliveryRetries.AsNoTracking().SingleAsync(r => r.OperationId == request.OperationId, token);
        if (audit.DeliveryId != id || audit.ActorUserId != actor || audit.Reason != reason || audit.AcknowledgeDuplicateRisk != request.AcknowledgeDuplicateRisk)
            throw new ApplicationFailureException(ApplicationError.IdempotencyConflict);
        if (inserted != 0)
        {
            if (row.Status != EmailDeliveryStatus.NeedsReview || row.RecipientEmail.Length == 0 || row.ReviewRetryCount == int.MaxValue)
                throw new ApplicationFailureException(ApplicationError.StateConflict);
            if (row.OutcomeUncertain && !request.AcknowledgeDuplicateRisk)
                throw new ApplicationFailureException(ApplicationError.ValidationFailed);
            row.Status = EmailDeliveryStatus.Pending; row.AttemptsInCycle = 0; row.ReviewRetryCount++;
            row.NextAttemptAt = now; row.UpdatedAt = now; row.LastErrorCode = null;
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token); return View(row);
    }
}
