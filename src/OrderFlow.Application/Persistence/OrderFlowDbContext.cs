using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Application.Persistence;

public class OrderFlowDbContext(DbContextOptions<OrderFlowDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);
        builder.Properties<decimal>().HavePrecision(18, 2);
        builder.Properties<UsdMoney>().HaveConversion<UsdMoneyConverter>().HavePrecision(18, 2);
        builder.Properties<DateTimeOffset>().HaveConversion<UtcTimestampConverter>()
            .HaveColumnType("timestamp with time zone");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<ApplicationUser>().ToTable("Users", "identity", table =>
            table.HasCheckConstraint("CK_Users_CreatedAt", "isfinite(\"CreatedAt\")"));
        builder.Entity<ApplicationUser>().Property(user => user.CreatedAt).IsRequired();
        builder.Entity<IdentityRole<Guid>>().ToTable("Roles", "identity");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "identity");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");
        builder.ApplyConfiguration(new IdempotencyRecordConfiguration());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
        builder.ApplyConfiguration(new ProcessedMessageConfiguration());
    }
}

public sealed class UtcTimestampConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => BusinessTime.ToUtcMicroseconds(value), value => BusinessTime.ToUtcMicroseconds(value));

public sealed class UsdMoneyConverter() : ValueConverter<UsdMoney, decimal>(
    value => value.Value, value => UsdMoney.FromDecimal(value));
