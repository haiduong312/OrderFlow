using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    public partial class AddOrderManagement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reviewed rename, not the scaffolded DROP/CREATE: preserve every existing audit row,
            // its primary/foreign keys and append-only triggers atomically under the migration lock.
            migrationBuilder.Sql("""
                ALTER TABLE ordering."OrderHistory" RENAME TO "OrderStatusHistory";
                ALTER TABLE ordering."OrderStatusHistory" RENAME CONSTRAINT "PK_OrderHistory" TO "PK_OrderStatusHistory";
                ALTER TABLE ordering."OrderStatusHistory" RENAME CONSTRAINT "CK_OrderHistory_Metadata" TO "CK_OrderStatusHistory_Metadata";
                ALTER TABLE ordering."OrderStatusHistory" RENAME CONSTRAINT "FK_OrderHistory_Orders_OrderId" TO "FK_OrderStatusHistory_Orders_OrderId";
                ALTER TABLE ordering."OrderStatusHistory" RENAME CONSTRAINT "FK_OrderHistory_Users_ActorUserId" TO "FK_OrderStatusHistory_Users_ActorUserId";
                ALTER INDEX ordering."IX_OrderHistory_ActorUserId" RENAME TO "IX_OrderStatusHistory_ActorUserId";
                ALTER TRIGGER "TR_OrderHistory_Immutable" ON ordering."OrderStatusHistory" RENAME TO "TR_OrderStatusHistory_Immutable";
                ALTER TRIGGER "TR_OrderHistory_NoTruncate" ON ordering."OrderStatusHistory" RENAME TO "TR_OrderStatusHistory_NoTruncate";
                """);
            migrationBuilder.AddColumn<string>(name: "FromStatus", schema: "ordering", table: "OrderStatusHistory",
                type: "character varying(32)", maxLength: 32, nullable: true);
            // Prior supported history consists of Customer checkout creation facts. A constant
            // ADD COLUMN default backfills without UPDATE (which the immutable trigger rejects).
            migrationBuilder.AddColumn<string>(name: "ActorKind", schema: "ordering", table: "OrderStatusHistory",
                type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Customer");
            migrationBuilder.Sql("ALTER TABLE ordering.\"OrderStatusHistory\" ALTER COLUMN \"ActorKind\" DROP DEFAULT;");
            migrationBuilder.AddCheckConstraint(name: "CK_OrderStatusHistory_Actor", schema: "ordering", table: "OrderStatusHistory",
                sql: "\"ActorKind\" IN ('Customer','Admin') AND (\"FromStatus\" IS NULL OR \"FromStatus\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired'))");
            migrationBuilder.AddColumn<string>(name: "ReleaseReason", schema: "inventory", table: "InventoryReservations",
                type: "character varying(16)", maxLength: 16, nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "ReleasedAt", schema: "inventory", table: "InventoryReservations",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.DropCheckConstraint(name: "CK_StockMovements_Kind", schema: "inventory", table: "StockMovements");
            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements",
                sql: "(\"Kind\" = 'Initialization' AND \"Operation\" = 'InitializeInventory:v1'\n AND \"OperationId\" = \"ProductId\" AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL\n AND \"Revision\" = 0 AND \"OnHandBefore\" = 0 AND \"OnHandAfter\" = 0\n AND \"ReservedBefore\" = 0 AND \"ReservedAfter\" = 0 AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" = 0\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" IN ('Receipt','Adjustment') AND \"Operation\" = 'ChangeStock:v1'\n AND \"ActorKind\" = 'Admin' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"ReservedDelta\" = 0 AND \"OnHandDelta\" <> 0\n AND (\"Kind\" <> 'Receipt' OR \"OnHandDelta\" > 0)\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" = 'Reservation' AND \"Operation\" = 'ReserveStock:v1'\n AND \"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN 1 AND 1000)\nOR\n(\"Kind\" = 'Release' AND \"Operation\" = 'ReleaseStock:v1'\n AND \"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN -1000 AND -1)");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt_Id",
                schema: "ordering",
                table: "Orders",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAt_Id",
                schema: "ordering",
                table: "Orders",
                columns: new[] { "Status", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Release",
                schema: "inventory",
                table: "InventoryReservations",
                sql: "(\"Status\" = 'Released' AND \"ReleasedAt\" IS NOT NULL AND isfinite(\"ReleasedAt\") AND \"ReleasedAt\" >= \"CreatedAt\" AND \"ReleaseReason\" IS NOT NULL AND \"ReleaseReason\" IN ('Cancelled','Expired')) OR (\"Status\" <> 'Released' AND \"ReleasedAt\" IS NULL AND \"ReleaseReason\" IS NULL)");


            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.protect_reservation_snapshot() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    IF ROW(NEW."Id", NEW."OrderId", NEW."ProductId", NEW."Quantity", NEW."CreatedAt", NEW."ExpiresAt")
                        IS DISTINCT FROM ROW(OLD."Id", OLD."OrderId", OLD."ProductId", OLD."Quantity", OLD."CreatedAt", OLD."ExpiresAt") THEN
                        RAISE EXCEPTION 'Reservation identity, quantity and deadline are immutable' USING ERRCODE = '55000';
                    END IF;
                    IF OLD."Status" <> 'Active' AND ROW(NEW."Status", NEW."ReleasedAt", NEW."ReleaseReason")
                        IS DISTINCT FROM ROW(OLD."Status", OLD."ReleasedAt", OLD."ReleaseReason") THEN
                        RAISE EXCEPTION 'A terminal reservation cannot change again' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Downgrading order management discards actor/release audit metadata. Use a reviewed forward migration.");
    }
}
