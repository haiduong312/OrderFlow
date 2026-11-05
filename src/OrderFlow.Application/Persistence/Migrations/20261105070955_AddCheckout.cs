using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.EnsureSchema(
                name: "ordering");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                schema: "inventory",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReservationId",
                schema: "inventory",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Deadline", "isfinite(\"CreatedAt\") AND isfinite(\"ExpiresAt\") AND \"ExpiresAt\" = \"CreatedAt\" + interval '15 minutes'");
                    table.CheckConstraint("CK_Orders_Money", "\"Currency\" = 'USD' AND \"Total\" > 0 AND \"Total\" <= 9999999999999999.99");
                    table.CheckConstraint("CK_Orders_State", "\"Version\" > 0 AND \"Status\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired')");
                    table.ForeignKey(
                        name: "FK_Orders_IdempotencyRecords_CheckoutRequestId",
                        column: x => x.CheckoutRequestId,
                        principalSchema: "messaging",
                        principalTable: "IdempotencyRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderHistory",
                schema: "ordering",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderHistory", x => new { x.OrderId, x.Version });
                    table.CheckConstraint("CK_OrderHistory_Metadata", "\"Version\" > 0 AND isfinite(\"OccurredAt\") AND length(btrim(\"Reason\")) > 0 AND \"Status\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired')");
                    table.ForeignKey(
                        name: "FK_OrderHistory_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderHistory_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                schema: "ordering",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => new { x.OrderId, x.ProductId });
                    table.CheckConstraint("CK_OrderItems_Money", "\"UnitPrice\" > 0 AND \"UnitPrice\" <= 9999999999999999.99 AND \"LineTotal\" > 0 AND \"LineTotal\" <= 9999999999999999.99 AND \"LineTotal\" = \"UnitPrice\" * \"Quantity\"");
                    table.CheckConstraint("CK_OrderItems_Quantity", "\"Quantity\" BETWEEN 1 AND 1000");
                    table.CheckConstraint("CK_OrderItems_Snapshot", "\"Sku\" ~ '^[A-Z0-9-]{1,64}$' AND length(btrim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryReservations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryReservations", x => x.Id);
                    table.UniqueConstraint("AK_InventoryReservations_Id_OrderId_ProductId", x => new { x.Id, x.OrderId, x.ProductId });
                    table.CheckConstraint("CK_Reservations_Deadline", "isfinite(\"CreatedAt\") AND isfinite(\"ExpiresAt\") AND \"ExpiresAt\" = \"CreatedAt\" + interval '15 minutes'");
                    table.CheckConstraint("CK_Reservations_Quantity", "\"Quantity\" BETWEEN 1 AND 1000");
                    table.CheckConstraint("CK_Reservations_State", "\"Status\" IN ('Active','Consumed','Released')");
                    table.ForeignKey(
                        name: "FK_InventoryReservations_Balances_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "inventory",
                        principalTable: "Balances",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryReservations_OrderItems_OrderId_ProductId",
                        columns: x => new { x.OrderId, x.ProductId },
                        principalSchema: "ordering",
                        principalTable: "OrderItems",
                        principalColumns: new[] { "OrderId", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReservationId_OrderId_ProductId",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "ReservationId", "OrderId", "ProductId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements",
                sql: "(\"Kind\" = 'Initialization' AND \"Operation\" = 'InitializeInventory:v1'\n AND \"OperationId\" = \"ProductId\" AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL\n AND \"Revision\" = 0 AND \"OnHandBefore\" = 0 AND \"OnHandAfter\" = 0\n AND \"ReservedBefore\" = 0 AND \"ReservedAfter\" = 0 AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" = 0\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" IN ('Receipt','Adjustment') AND \"Operation\" = 'ChangeStock:v1'\n AND \"ActorKind\" = 'Admin' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"ReservedDelta\" = 0 AND \"OnHandDelta\" <> 0\n AND (\"Kind\" <> 'Receipt' OR \"OnHandDelta\" > 0)\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" = 'Reservation' AND \"Operation\" = 'ReserveStock:v1'\n AND \"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN 1 AND 1000)");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_OrderId_ProductId",
                schema: "inventory",
                table: "InventoryReservations",
                columns: new[] { "OrderId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryReservations_ProductId",
                schema: "inventory",
                table: "InventoryReservations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHistory_ActorUserId",
                schema: "ordering",
                table: "OrderHistory",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                schema: "ordering",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CheckoutRequestId",
                schema: "ordering",
                table: "Orders",
                column: "CheckoutRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ExpiresAt_Id",
                schema: "ordering",
                table: "Orders",
                columns: new[] { "ExpiresAt", "Id" },
                filter: "\"Status\" = 'PendingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_CreatedAt_Id",
                schema: "ordering",
                table: "Orders",
                columns: new[] { "UserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_InventoryReservations_ReservationId_OrderId_~",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "ReservationId", "OrderId", "ProductId" },
                principalSchema: "inventory",
                principalTable: "InventoryReservations",
                principalColumns: new[] { "Id", "OrderId", "ProductId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION ordering.reject_snapshot_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'Order snapshots and history are append only' USING ERRCODE = '55000';
                END;
                $body$;
                CREATE TRIGGER "TR_OrderItems_Immutable" BEFORE UPDATE OR DELETE ON ordering."OrderItems"
                FOR EACH ROW EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_OrderItems_NoTruncate" BEFORE TRUNCATE ON ordering."OrderItems"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_OrderHistory_Immutable" BEFORE UPDATE OR DELETE ON ordering."OrderHistory"
                FOR EACH ROW EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_OrderHistory_NoTruncate" BEFORE TRUNCATE ON ordering."OrderHistory"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();

                CREATE FUNCTION ordering.protect_order_snapshot() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    IF ROW(NEW."Id", NEW."UserId", NEW."CheckoutRequestId", NEW."Currency", NEW."Total", NEW."CreatedAt", NEW."ExpiresAt")
                        IS DISTINCT FROM ROW(OLD."Id", OLD."UserId", OLD."CheckoutRequestId", OLD."Currency", OLD."Total", OLD."CreatedAt", OLD."ExpiresAt") THEN
                        RAISE EXCEPTION 'Order checkout facts are immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER "TR_Orders_ProtectSnapshot" BEFORE UPDATE ON ordering."Orders"
                FOR EACH ROW EXECUTE FUNCTION ordering.protect_order_snapshot();

                CREATE FUNCTION inventory.protect_reservation_snapshot() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    IF ROW(NEW."Id", NEW."OrderId", NEW."ProductId", NEW."Quantity", NEW."CreatedAt", NEW."ExpiresAt")
                        IS DISTINCT FROM ROW(OLD."Id", OLD."OrderId", OLD."ProductId", OLD."Quantity", OLD."CreatedAt", OLD."ExpiresAt") THEN
                        RAISE EXCEPTION 'Reservation identity, quantity and deadline are immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER "TR_Reservations_ProtectSnapshot" BEFORE UPDATE ON inventory."InventoryReservations"
                FOR EACH ROW EXECUTE FUNCTION inventory.protect_reservation_snapshot();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_OrderItems_Immutable" ON ordering."OrderItems";
                DROP TRIGGER "TR_OrderItems_NoTruncate" ON ordering."OrderItems";
                DROP TRIGGER "TR_OrderHistory_Immutable" ON ordering."OrderHistory";
                DROP TRIGGER "TR_OrderHistory_NoTruncate" ON ordering."OrderHistory";
                DROP TRIGGER "TR_Orders_ProtectSnapshot" ON ordering."Orders";
                DROP TRIGGER "TR_Reservations_ProtectSnapshot" ON inventory."InventoryReservations";
                DROP FUNCTION ordering.reject_snapshot_mutation();
                DROP FUNCTION ordering.protect_order_snapshot();
                DROP FUNCTION inventory.protect_reservation_snapshot();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_InventoryReservations_ReservationId_OrderId_~",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "InventoryReservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "OrderHistory",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "OrderItems",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "ordering");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ReservationId_OrderId_ProductId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "OrderId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements",
                sql: "(\"Kind\" = 'Initialization' AND \"Operation\" = 'InitializeInventory:v1'\n AND \"OperationId\" = \"ProductId\" AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL\n AND \"Revision\" = 0 AND \"OnHandBefore\" = 0 AND \"OnHandAfter\" = 0\n AND \"ReservedBefore\" = 0 AND \"ReservedAfter\" = 0 AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" = 0)\nOR\n(\"Kind\" IN ('Receipt','Adjustment') AND \"Operation\" = 'ChangeStock:v1'\n AND \"ActorKind\" = 'Admin' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"ReservedDelta\" = 0 AND \"OnHandDelta\" <> 0\n AND (\"Kind\" <> 'Receipt' OR \"OnHandDelta\" > 0))");
        }
    }
}
