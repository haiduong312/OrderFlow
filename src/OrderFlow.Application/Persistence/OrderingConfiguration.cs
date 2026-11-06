using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Application.Persistence;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", "ordering", table =>
        {
            table.HasCheckConstraint("CK_Orders_Money", "\"Currency\" = 'USD' AND \"Total\" > 0 AND \"Total\" <= 9999999999999999.99");
            table.HasCheckConstraint("CK_Orders_Deadline", "isfinite(\"CreatedAt\") AND isfinite(\"ExpiresAt\") AND \"ExpiresAt\" = \"CreatedAt\" + interval '15 minutes'");
            table.HasCheckConstraint("CK_Orders_State", "\"Version\" > 0 AND \"Status\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired')");
        });
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.Currency).HasMaxLength(3).IsRequired();
        builder.Property(order => order.Version).IsConcurrencyToken();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(order => order.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdempotencyRecord>().WithOne().HasForeignKey<Order>(order => order.CheckoutRequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(order => new { order.UserId, order.CreatedAt, order.Id }).IsDescending(false, true, true);
        builder.HasIndex(order => new { order.CreatedAt, order.Id }).IsDescending(true, true);
        builder.HasIndex(order => new { order.Status, order.CreatedAt, order.Id }).IsDescending(false, true, true);
        builder.HasIndex(order => new { order.ExpiresAt, order.Id }).HasFilter("\"Status\" = 'PendingPayment'");
        foreach (var name in new[] { nameof(Order.UserId), nameof(Order.CheckoutRequestId), nameof(Order.Currency), nameof(Order.Total), nameof(Order.CreatedAt), nameof(Order.ExpiresAt) })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", "ordering", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" BETWEEN 1 AND 1000");
            table.HasCheckConstraint("CK_OrderItems_Money", "\"UnitPrice\" > 0 AND \"UnitPrice\" <= 9999999999999999.99 AND \"LineTotal\" > 0 AND \"LineTotal\" <= 9999999999999999.99 AND \"LineTotal\" = \"UnitPrice\" * \"Quantity\"");
            table.HasCheckConstraint("CK_OrderItems_Snapshot", "\"Sku\" ~ '^[A-Z0-9-]{1,64}$' AND length(btrim(\"Name\")) > 0");
        });
        builder.HasKey(item => new { item.OrderId, item.ProductId });
        builder.Property(item => item.Sku).IsRequired().HasMaxLength(64).UseCollation("C");
        builder.Property(item => item.Name).IsRequired().HasMaxLength(200);
        builder.HasOne<Product>().WithMany().HasForeignKey(item => item.ProductId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory", "ordering", table =>
        {
            table.HasCheckConstraint("CK_OrderStatusHistory_Metadata", "\"Version\" > 0 AND isfinite(\"OccurredAt\") AND length(btrim(\"Reason\")) > 0 AND \"Status\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired')");
            table.HasCheckConstraint("CK_OrderStatusHistory_Actor", "((\"ActorKind\" IN ('Customer','Admin') AND \"ActorUserId\" IS NOT NULL) OR (\"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL AND \"FromStatus\" IS NOT NULL AND \"FromStatus\" = 'PendingPayment' AND \"Status\" = 'Expired')) AND (\"FromStatus\" IS NULL OR \"FromStatus\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired'))");
        });
        builder.HasKey(history => new { history.OrderId, history.Version });
        builder.Property(history => history.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(history => history.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(history => history.ActorKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(history => history.Reason).HasMaxLength(500).IsRequired();
        builder.HasOne<Order>().WithMany().HasForeignKey(history => history.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(history => history.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", "inventory", table =>
        {
            table.HasCheckConstraint("CK_Reservations_Quantity", "\"Quantity\" BETWEEN 1 AND 1000");
            table.HasCheckConstraint("CK_Reservations_State", "\"Status\" IN ('Active','Consumed','Released')");
            table.HasCheckConstraint("CK_Reservations_Deadline", "isfinite(\"CreatedAt\") AND isfinite(\"ExpiresAt\") AND \"ExpiresAt\" = \"CreatedAt\" + interval '15 minutes'");
            table.HasCheckConstraint("CK_Reservations_Release", "(\"Status\" = 'Released' AND \"ReleasedAt\" IS NOT NULL AND isfinite(\"ReleasedAt\") AND \"ReleasedAt\" >= \"CreatedAt\" AND \"ReleaseReason\" IS NOT NULL AND \"ReleaseReason\" IN ('Cancelled','Expired')) OR (\"Status\" <> 'Released' AND \"ReleasedAt\" IS NULL AND \"ReleaseReason\" IS NULL)");
        });
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();
        builder.HasIndex(reservation => new { reservation.OrderId, reservation.ProductId }).IsUnique();
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(16).IsRequired().IsConcurrencyToken();
        builder.Property(reservation => reservation.ReleaseReason).HasConversion<string>().HasMaxLength(16);
        builder.HasOne<OrderItem>().WithOne().HasForeignKey<InventoryReservation>(reservation => new { reservation.OrderId, reservation.ProductId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryBalance>().WithMany().HasForeignKey(reservation => reservation.ProductId).OnDelete(DeleteBehavior.Restrict);
        foreach (var name in new[] { nameof(InventoryReservation.OrderId), nameof(InventoryReservation.ProductId), nameof(InventoryReservation.Quantity), nameof(InventoryReservation.CreatedAt), nameof(InventoryReservation.ExpiresAt) })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
