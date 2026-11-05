using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Pagination;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Inventory;

public sealed class InventoryService(OrderFlowDbContext database, ICurrentUser currentUser, TimeProvider clock)
{
    public Task<StockMovementResponse> ReceiveAsync(Guid productId, StockReceiptRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(productId, request.OperationId, StockMovementKind.Receipt,
            request.Quantity ?? throw Invalid("quantity", "Quantity is required."), request.Reason, cancellationToken);

    public Task<StockMovementResponse> AdjustAsync(Guid productId, StockAdjustmentRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(productId, request.OperationId, StockMovementKind.Adjustment,
            request.Delta ?? throw Invalid("delta", "Delta is required."), request.Reason, cancellationToken);

    public async Task<InventoryResponse> GetAsync(Guid productId, CancellationToken cancellationToken) =>
        await database.InventoryBalances.AsNoTracking().Where(balance => balance.ProductId == productId)
            .Select(balance => new InventoryResponse(balance.ProductId, balance.OnHand, balance.Reserved, balance.Revision))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new ApplicationFailureException(ApplicationError.NotFound);

    public async Task<StockHistoryResponse> HistoryAsync(Guid productId, StockHistoryQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize);
        if (!await database.InventoryBalances.AsNoTracking().AnyAsync(balance => balance.ProductId == productId, cancellationToken))
            throw new ApplicationFailureException(ApplicationError.NotFound);
        var query = database.StockMovements.AsNoTracking().Where(movement => movement.ProductId == productId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(movement => movement.Revision).Skip(page.Offset).Take(page.PageSize).ToListAsync(cancellationToken);
        return new(rows.Select(Response).ToArray(), page.Page, page.PageSize, total);
    }

    private async Task<StockMovementResponse> ChangeAsync(Guid productId, Guid operationId, StockMovementKind kind,
        int delta, string reason, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        StockMovement.ValidateCommand(operationId, kind, delta, actorId);
        var normalizedReason = StockMovement.NormalizeReason(reason);
        if (productId == Guid.Empty) throw Invalid("productId", "A nonempty product ID is required.");
        if (!await database.Users.AsNoTracking().AnyAsync(user => user.Id == actorId, cancellationToken))
            throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        var prior = await PriorAsync(operationId, cancellationToken);
        if (prior is not null) return Replay(prior, productId, kind, delta, normalizedReason, actorId);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var balance = (await InventoryLocking.LockAsync(database, [productId], cancellationToken))[0];
        // A same-product duplicate may have committed while this request waited for the lock.
        prior = await PriorAsync(operationId, cancellationToken);
        if (prior is not null) return Replay(prior, productId, kind, delta, normalizedReason, actorId);
        var movement = balance.Change(operationId, kind, delta, normalizedReason, actorId, BusinessTime.GetUtcNow(clock));
        database.StockMovements.Add(movement);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Response(movement);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_StockMovements_Operation" })
        {
            // Cross-product duplicates use different balance locks but share a durable operation identity.
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            database.Entry(movement).State = EntityState.Detached;
            await database.Entry(balance).ReloadAsync(cancellationToken);
            prior = await PriorAsync(operationId, cancellationToken)
                ?? throw new InvalidOperationException("A conflicting stock operation has no committed movement.");
            return Replay(prior, productId, kind, delta, normalizedReason, actorId);
        }
    }

    private Task<StockMovement?> PriorAsync(Guid operationId, CancellationToken cancellationToken) =>
        database.StockMovements.AsNoTracking().SingleOrDefaultAsync(movement => movement.Operation == StockMovement.StockChangeOperation
            && movement.OperationId == operationId, cancellationToken);

    private static StockMovementResponse Replay(StockMovement movement, Guid productId, StockMovementKind kind, int delta, string reason, Guid actorId)
    {
        if (movement.ProductId != productId || movement.Kind != kind || movement.OnHandDelta != delta
            || movement.Reason != reason || movement.ActorUserId != actorId)
            throw new ApplicationFailureException(ApplicationError.IdempotencyConflict);
        return Response(movement); // Original immutable result, never today's balance.
    }

    private static StockMovementResponse Response(StockMovement movement) => new(movement.Id, movement.ProductId, movement.Operation,
        movement.OperationId, movement.Kind, movement.ActorKind, movement.ActorUserId, movement.Reason, movement.OnHandDelta,
        movement.ReservedDelta, movement.OnHandBefore, movement.ReservedBefore, movement.OnHandAfter, movement.ReservedAfter,
        movement.Revision, movement.OccurredAt);

    private static ApplicationFailureException Invalid(string field, string message) =>
        new(ApplicationError.ValidationFailed, new Dictionary<string, string[]> { [field] = [message] });
}
