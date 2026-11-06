using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Modules.Payments.Simulation;

namespace OrderFlow.Application.Persistence;

public class OrderFlowDbContext(DbContextOptions<OrderFlowDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<ProviderPayment> ProviderPayments => Set<ProviderPayment>();
    public DbSet<ProviderScenario> ProviderScenarios => Set<ProviderScenario>();
    public DbSet<ProviderRefund> ProviderRefunds => Set<ProviderRefund>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

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
        builder.ApplyConfiguration(new PaymentAttemptConfiguration());
        builder.ApplyConfiguration(new ProviderPaymentConfiguration());
        builder.ApplyConfiguration(new ProviderScenarioConfiguration());
        builder.ApplyConfiguration(new ProviderRefundConfiguration());
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
        builder.ApplyConfiguration(new ProductConfiguration());
        builder.ApplyConfiguration(new CategoryConfiguration());
        builder.ApplyConfiguration(new InventoryBalanceConfiguration());
        builder.ApplyConfiguration(new StockMovementConfiguration());
        builder.ApplyConfiguration(new OrderConfiguration());
        builder.ApplyConfiguration(new OrderItemConfiguration());
        builder.ApplyConfiguration(new OrderStatusHistoryConfiguration());
        builder.ApplyConfiguration(new InventoryReservationConfiguration());
    }
}

public sealed class UtcTimestampConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => BusinessTime.ToUtcMicroseconds(value), value => BusinessTime.ToUtcMicroseconds(value));

public sealed class UsdMoneyConverter() : ValueConverter<UsdMoney, decimal>(
    value => value.Value, value => UsdMoney.FromDecimal(value));
