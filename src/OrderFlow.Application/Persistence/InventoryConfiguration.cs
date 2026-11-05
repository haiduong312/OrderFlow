using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Modules.Inventory;

namespace OrderFlow.Application.Persistence;

public sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("Balances", "inventory", table =>
        {
            table.HasCheckConstraint("CK_Balances_Quantities", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
            table.HasCheckConstraint("CK_Balances_Revision", "\"Revision\" >= 0");
        });
        builder.HasKey(balance => balance.ProductId);
        builder.Property(balance => balance.ProductId).ValueGeneratedNever();
        builder.Property(balance => balance.Revision).IsConcurrencyToken();
        builder.Ignore(balance => balance.Available);
        builder.HasOne<Product>().WithOne(product => product.Inventory).HasForeignKey<InventoryBalance>(balance => balance.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", "inventory", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_Quantities", """
                "OnHandBefore" >= 0 AND "ReservedBefore" >= 0 AND "ReservedBefore" <= "OnHandBefore"
                AND "OnHandAfter" >= 0 AND "ReservedAfter" >= 0 AND "ReservedAfter" <= "OnHandAfter"
                AND "OnHandBefore"::bigint + "OnHandDelta" = "OnHandAfter"::bigint
                AND "ReservedBefore"::bigint + "ReservedDelta" = "ReservedAfter"::bigint
                """);
            table.HasCheckConstraint("CK_StockMovements_Reason", "\"Reason\" ~ '[^[:space:]]'");
            table.HasCheckConstraint("CK_StockMovements_OccurredAt", "isfinite(\"OccurredAt\")");
            table.HasCheckConstraint("CK_StockMovements_OperationId", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_StockMovements_Kind", """
                ("Kind" = 'Initialization' AND "Operation" = 'InitializeInventory:v1'
                 AND "OperationId" = "ProductId" AND "ActorKind" = 'System' AND "ActorUserId" IS NULL
                 AND "Revision" = 0 AND "OnHandBefore" = 0 AND "OnHandAfter" = 0
                 AND "ReservedBefore" = 0 AND "ReservedAfter" = 0 AND "OnHandDelta" = 0 AND "ReservedDelta" = 0
                 AND "OrderId" IS NULL AND "ReservationId" IS NULL)
                OR
                ("Kind" IN ('Receipt','Adjustment') AND "Operation" = 'ChangeStock:v1'
                 AND "ActorKind" = 'Admin' AND "ActorUserId" IS NOT NULL AND "Revision" > 0
                 AND "ReservedDelta" = 0 AND "OnHandDelta" <> 0
                 AND ("Kind" <> 'Receipt' OR "OnHandDelta" > 0)
                 AND "OrderId" IS NULL AND "ReservationId" IS NULL)
                OR
                ("Kind" = 'Reservation' AND "Operation" = 'ReserveStock:v1'
                 AND "ActorKind" = 'Customer' AND "ActorUserId" IS NOT NULL AND "Revision" > 0
                 AND "OrderId" IS NOT NULL AND "ReservationId" IS NOT NULL AND "OperationId" = "ReservationId"
                 AND "OnHandDelta" = 0 AND "ReservedDelta" BETWEEN 1 AND 1000)
                """);
        });
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Id).ValueGeneratedNever();
        builder.Property(movement => movement.Operation).IsRequired().HasMaxLength(50).UseCollation("C");
        builder.Property(movement => movement.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(movement => movement.ActorKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(movement => movement.Reason).IsRequired().HasMaxLength(500);
        builder.HasOne<InventoryBalance>().WithMany().HasForeignKey(movement => movement.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(movement => movement.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryReservation>().WithMany()
            .HasForeignKey(movement => new { movement.ReservationId, movement.OrderId, movement.ProductId })
            .HasPrincipalKey(reservation => new { reservation.Id, reservation.OrderId, reservation.ProductId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(movement => new { movement.Operation, movement.OperationId }).IsUnique().HasDatabaseName("UX_StockMovements_Operation");
        builder.HasIndex(movement => new { movement.ProductId, movement.Revision }).IsUnique().IsDescending(false, true)
            .HasDatabaseName("UX_StockMovements_Product_Revision");
        foreach (var property in builder.Metadata.GetProperties()) property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
