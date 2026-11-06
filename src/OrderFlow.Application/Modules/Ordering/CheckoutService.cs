using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

public sealed class CheckoutService(OrderFlowDbContext database, ICurrentUser currentUser, TimeProvider clock, ILogger<CheckoutService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CheckoutResponse> CreateAsync(string? key, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        var validatedKey = CheckoutRequest.ValidateKey(key);
        var normalized = CheckoutRequest.Normalize(request);
        if (!await database.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken))
            throw new ApplicationFailureException(ApplicationError.Unauthenticated);
        try
        {
            return await CreateOrReplayAsync(userId, validatedKey, normalized, cancellationToken);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested && OrderDatabase.Contention(failure) is not null)
        {
            var state = OrderDatabase.Contention(failure)!.SqlState;
            logger.LogWarning(new EventId(1201, "CheckoutContention"), "Checkout transaction failed with PostgreSQL state {SqlState}", state);
            // No implicit retry, especially around an uncertain commit. The caller keeps the same key.
            throw new ApplicationFailureException(ApplicationError.ServiceUnavailable);
        }
    }

    private async Task<CheckoutResponse> CreateOrReplayAsync(Guid userId, string key, CheckoutRequest request, CancellationToken cancellationToken)
    {
        var prior = await PriorAsync(userId, key, cancellationToken);
        if (prior is not null) return Replay(prior, request);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Explicit transaction-local budgets, restored automatically on commit/rollback.
        await OrderDatabase.SetTimeoutsAsync(database, cancellationToken);
        var claim = IdempotencyRecord.Create(userId, CheckoutRequest.Operation, key, request.CanonicalBody, BusinessTime.GetUtcNow(clock));
        database.IdempotencyRecords.Add(claim);
        try
        {
            // Arbitrate BEFORE catalog/stock locks. The unique user/operation/key index waits for
            // an uncommitted contender; if that contender rolls back this INSERT may succeed.
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_Idempotency_User_Operation_Key" })
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            database.Entry(claim).State = EntityState.Detached;
            // Never query in the failed transaction, or leave the losing Added claim tracked.
            var winner = await PriorAsync(userId, key, cancellationToken)
                ?? throw new InvalidOperationException("The conflicting checkout has no committed response.");
            return Replay(winner, request);
        }

        var products = await LockCatalogAsync(request.Items.Select(item => item.ProductId).ToArray(), cancellationToken);
        var balances = await InventoryLocking.LockAsync(database, request.Items.Select(item => item.ProductId), cancellationToken);
        // Time/price eligibility is decided after all checkout locks. Waiting does not consume
        // the new order's 15-minute lifetime, and a replay never constructs another deadline.
        var now = BusinessTime.GetUtcNow(clock);
        var order = Order.Create(userId, claim.Id, request.Items.Select(item =>
        {
            var product = products[item.ProductId];
            return new OrderItemSnapshot(product.Id, product.Sku, product.Name, product.Price, item.Quantity);
        }).ToArray(), now);
        database.Orders.Add(order);
        database.OrderStatusHistory.Add(OrderStatusHistory.Created(order));
        await database.SaveChangesAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            var reservation = InventoryReservation.Create(order.Id, item.ProductId, item.Quantity, now);
            var balance = balances.Single(balance => balance.ProductId == item.ProductId);
            var movement = balance.Reserve(reservation, userId);
            database.InventoryReservations.Add(reservation);
            database.StockMovements.Add(movement);
            // Persist each effect under the SAME transaction. A failure on a later item rolls
            // back even earlier SQL writes, not just pending tracked changes.
            await database.SaveChangesAsync(cancellationToken);
        }

        var response = OrderViews.Checkout(order);
        database.OutboxMessages.Add(OrderEvents.Create(order, "ordering.order-created", now, claim.Id));
        var body = JsonSerializer.SerializeToUtf8Bytes(response, Json);
        var location = "/api/orders/" + order.Id.ToString("D");
        claim.Complete(201, "application/json; charset=utf-8", body, location, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(201, claim.ResponseContentType!, location, body);
    }

    private async Task<Dictionary<Guid, Product>> LockCatalogAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        // Discover associations without locking; revalidate them after acquiring all locks.
        var associations = await database.Products.AsNoTracking().Where(product => ids.Contains(product.Id))
            .Select(product => new { product.Id, product.CategoryId }).ToListAsync(cancellationToken);
        if (associations.Count != ids.Length) throw new ApplicationFailureException(ApplicationError.NotFound);
        var categories = new Dictionary<Guid, Category>();
        foreach (var id in associations.Select(product => product.CategoryId).Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal))
        {
            // SHARE blocks catalog edits/archival but is compatible with another checkout.
            var category = await database.Categories.FromSqlInterpolated($"""
                SELECT * FROM catalog."Categories" WHERE "Id" = {id} FOR SHARE
                """).AsNoTracking().SingleAsync(cancellationToken);
            categories.Add(id, category);
        }
        var products = new Dictionary<Guid, Product>();
        foreach (var id in ids) // already canonical UUID order
        {
            var product = await database.Products.FromSqlInterpolated($"""
                SELECT * FROM catalog."Products" WHERE "Id" = {id} FOR SHARE
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                ?? throw new ApplicationFailureException(ApplicationError.NotFound);
            // An assignment changed between discovery and lock acquisition. Never acquire a
            // new earlier-level category lock now; roll back and let the caller retry its key.
            if (!categories.TryGetValue(product.CategoryId, out var category) || !category.IsActive || !product.IsActive)
                throw new ApplicationFailureException(ApplicationError.StateConflict);
            products.Add(id, product);
        }
        return products;
    }

    private Task<IdempotencyRecord?> PriorAsync(Guid userId, string key, CancellationToken cancellationToken) =>
        database.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record => record.UserId == userId
            && record.Operation == CheckoutRequest.Operation && record.Key == key, cancellationToken);

    private static CheckoutResponse Replay(IdempotencyRecord prior, CheckoutRequest request)
    {
        if (prior.CanonicalRequest != request.CanonicalBody || !prior.RequestHash.AsSpan().SequenceEqual(request.Hash))
            throw new ApplicationFailureException(ApplicationError.IdempotencyConflict);
        if (prior.Status != IdempotencyStatus.Completed || prior.ResponseStatusCode is null || prior.ResponseContentType is null
            || prior.ResponseLocation is null || prior.ResponseBody is null)
            throw new InvalidOperationException("A committed checkout claim must contain its completed response.");
        return new(prior.ResponseStatusCode.Value, prior.ResponseContentType, prior.ResponseLocation, prior.ResponseBody);
    }

}
