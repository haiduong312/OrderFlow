using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Inventory;

public static class InventoryLocking
{
    public static IReadOnlyList<Guid> OrderProductIds(IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().Take(101).ToArray();
        if (ids.Length is < 1 or > 100 || ids.Contains(Guid.Empty))
            throw new ArgumentException("Lock 1–100 distinct nonempty product IDs.", nameof(productIds));
        // Canonical UUID byte order agrees with PostgreSQL UUID ordering.
        return ids.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
    }

    public static async Task<IReadOnlyList<InventoryBalance>> LockAsync(OrderFlowDbContext database,
        IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Inventory locks require an active unit-of-work transaction.");
        var ordered = OrderProductIds(productIds);
        var entries = database.ChangeTracker.Entries<InventoryBalance>()
            .Where(entry => ordered.Contains(entry.Entity.ProductId)).ToDictionary(entry => entry.Entity.ProductId);
        if (entries.Values.Any(entry => entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("Save pending inventory changes before acquiring fresh inventory locks.");
        var result = new List<InventoryBalance>();
        foreach (var id in ordered)
        {
            var fresh = await database.InventoryBalances.FromSqlInterpolated($"""
                SELECT * FROM inventory."Balances" WHERE "ProductId" = {id} FOR UPDATE
                """).AsNoTracking().SingleOrDefaultAsync(cancellationToken)
                ?? throw new ApplicationFailureException(ApplicationError.NotFound);
            if (entries.TryGetValue(id, out var tracked))
            {
                // Deliberately refresh both snapshots under the row lock. Never let an old
                // tracked balance overwrite stock committed by a different unit of work.
                tracked.CurrentValues.SetValues(fresh);
                tracked.OriginalValues.SetValues(fresh);
                tracked.State = EntityState.Unchanged;
                result.Add(tracked.Entity);
            }
            else
            {
                database.Attach(fresh);
                result.Add(fresh);
            }
        }
        return result;
    }
}
