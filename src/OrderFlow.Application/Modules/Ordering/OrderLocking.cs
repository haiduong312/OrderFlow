using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Ordering;

public static class OrderLocking
{
    public static async Task<Order> LockAsync(OrderFlowDbContext database, Guid id, Guid? ownerId, CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Order locks require an active transaction.");
        var query = ownerId is { } owner
            ? database.Orders.FromSqlInterpolated($"""
                SELECT * FROM ordering."Orders" WHERE "Id" = {id} AND "UserId" = {owner} FOR UPDATE
                """)
            : database.Orders.FromSqlInterpolated($"""
                SELECT * FROM ordering."Orders" WHERE "Id" = {id} FOR UPDATE
                """);
        var fresh = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApplicationFailureException(ApplicationError.NotFound);
        var tracked = database.ChangeTracker.Entries<Order>().SingleOrDefault(entry => entry.Entity.Id == id);
        if (tracked is null) { database.Attach(fresh); return fresh; }
        if (tracked.State != EntityState.Unchanged) throw new InvalidOperationException("Save pending order changes before locking.");
        tracked.CurrentValues.SetValues(fresh);
        tracked.OriginalValues.SetValues(fresh);
        return tracked.Entity;
    }
}
