using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemOrderExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_Actor",
                schema: "ordering",
                table: "OrderStatusHistory");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                schema: "ordering",
                table: "OrderStatusHistory",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements",
                sql: "(\"Kind\" = 'Initialization' AND \"Operation\" = 'InitializeInventory:v1'\n AND \"OperationId\" = \"ProductId\" AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL\n AND \"Revision\" = 0 AND \"OnHandBefore\" = 0 AND \"OnHandAfter\" = 0\n AND \"ReservedBefore\" = 0 AND \"ReservedAfter\" = 0 AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" = 0\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" IN ('Receipt','Adjustment') AND \"Operation\" = 'ChangeStock:v1'\n AND \"ActorKind\" = 'Admin' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"ReservedDelta\" = 0 AND \"OnHandDelta\" <> 0\n AND (\"Kind\" <> 'Receipt' OR \"OnHandDelta\" > 0)\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" = 'Reservation' AND \"Operation\" = 'ReserveStock:v1'\n AND \"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN 1 AND 1000)\nOR\n(\"Kind\" = 'Release' AND \"Operation\" = 'ReleaseStock:v1'\n AND ((\"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL)\n      OR (\"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL AND \"Reason\" = 'Reservation released: Expired')) AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN -1000 AND -1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_Actor",
                schema: "ordering",
                table: "OrderStatusHistory",
                sql: "((\"ActorKind\" IN ('Customer','Admin') AND \"ActorUserId\" IS NOT NULL) OR (\"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL AND \"FromStatus\" IS NOT NULL AND \"FromStatus\" = 'PendingPayment' AND \"Status\" = 'Expired')) AND (\"FromStatus\" IS NULL OR \"FromStatus\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Downgrading system expiry cannot preserve nullable system audit actors. Use a reviewed forward migration.");
    }
}
