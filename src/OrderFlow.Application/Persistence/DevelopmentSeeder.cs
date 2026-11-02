using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Application.Persistence;

public static class DevelopmentSeeder
{
    // Seed role definitions only. No users, passwords, admin assignment or catalog fixtures.
    public static async Task SeedRolesAsync(OrderFlowDbContext context, CancellationToken cancellationToken)
    {
        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException("Apply the dedicated migration step before development seeding.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var (id, name, normalized) in new[]
        {
            (Guid.Parse("6de3bcb2-7d8c-4f75-84ad-a0ea7e21851c"), "Admin", "ADMIN"),
            (Guid.Parse("e9b41e3d-c046-48a2-950d-a3189addc48e"), "Customer", "CUSTOMER")
        })
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity."Roles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                VALUES ({id}, {name}, {normalized}, {Guid.NewGuid().ToString()})
                ON CONFLICT ("NormalizedName") DO NOTHING
                """, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
