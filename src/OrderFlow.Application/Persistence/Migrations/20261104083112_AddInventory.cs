using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "Balances",
                schema: "inventory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnHand = table.Column<int>(type: "integer", nullable: false),
                    Reserved = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Balances", x => x.ProductId);
                    table.CheckConstraint("CK_Balances_Quantities", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\"");
                    table.CheckConstraint("CK_Balances_Revision", "\"Revision\" >= 0");
                    table.ForeignKey(
                        name: "FK_Balances_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, collation: "C"),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OnHandDelta = table.Column<int>(type: "integer", nullable: false),
                    ReservedDelta = table.Column<int>(type: "integer", nullable: false),
                    OnHandBefore = table.Column<int>(type: "integer", nullable: false),
                    ReservedBefore = table.Column<int>(type: "integer", nullable: false),
                    OnHandAfter = table.Column<int>(type: "integer", nullable: false),
                    ReservedAfter = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Kind", "(\"Kind\" = 'Initialization' AND \"Operation\" = 'InitializeInventory:v1'\n AND \"OperationId\" = \"ProductId\" AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL\n AND \"Revision\" = 0 AND \"OnHandBefore\" = 0 AND \"OnHandAfter\" = 0\n AND \"ReservedBefore\" = 0 AND \"ReservedAfter\" = 0 AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" = 0)\nOR\n(\"Kind\" IN ('Receipt','Adjustment') AND \"Operation\" = 'ChangeStock:v1'\n AND \"ActorKind\" = 'Admin' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"ReservedDelta\" = 0 AND \"OnHandDelta\" <> 0\n AND (\"Kind\" <> 'Receipt' OR \"OnHandDelta\" > 0))");
                    table.CheckConstraint("CK_StockMovements_OccurredAt", "isfinite(\"OccurredAt\")");
                    table.CheckConstraint("CK_StockMovements_OperationId", "\"OperationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_StockMovements_Quantities", "\"OnHandBefore\" >= 0 AND \"ReservedBefore\" >= 0 AND \"ReservedBefore\" <= \"OnHandBefore\"\nAND \"OnHandAfter\" >= 0 AND \"ReservedAfter\" >= 0 AND \"ReservedAfter\" <= \"OnHandAfter\"\nAND \"OnHandBefore\"::bigint + \"OnHandDelta\" = \"OnHandAfter\"::bigint\nAND \"ReservedBefore\"::bigint + \"ReservedDelta\" = \"ReservedAfter\"::bigint");
                    table.CheckConstraint("CK_StockMovements_Reason", "\"Reason\" ~ '[^[:space:]]'");
                    table.ForeignKey(
                        name: "FK_StockMovements_Balances_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "inventory",
                        principalTable: "Balances",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ActorUserId",
                schema: "inventory",
                table: "StockMovements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_Operation",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "Operation", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_Product_Revision",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "ProductId", "Revision" },
                unique: true,
                descending: new[] { false, true });

            migrationBuilder.Sql("""
                CREATE FUNCTION inventory.initialize_product_inventory() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    INSERT INTO inventory."Balances" ("ProductId", "OnHand", "Reserved", "Revision")
                    VALUES (NEW."Id", 0, 0, 0);
                    INSERT INTO inventory."StockMovements"
                        ("Id", "ProductId", "Operation", "OperationId", "Kind", "ActorKind", "ActorUserId", "Reason",
                         "OnHandDelta", "ReservedDelta", "OnHandBefore", "ReservedBefore", "OnHandAfter", "ReservedAfter", "Revision", "OccurredAt")
                    VALUES (gen_random_uuid(), NEW."Id", 'InitializeInventory:v1', NEW."Id", 'Initialization', 'System', NULL,
                            'Product inventory initialized at zero', 0, 0, 0, 0, 0, 0, 0, NEW."CreatedAt");
                    RETURN NEW;
                END;
                $body$;
                CREATE TRIGGER "TR_Products_InitializeInventory" AFTER INSERT ON catalog."Products"
                FOR EACH ROW EXECUTE FUNCTION inventory.initialize_product_inventory();

                -- The trigger is installed under the migration transaction's table lock.
                -- Existing products receive the same zero opening facts; no stock is invented.
                INSERT INTO inventory."Balances" ("ProductId", "OnHand", "Reserved", "Revision")
                SELECT "Id", 0, 0, 0 FROM catalog."Products" ORDER BY "Id";
                INSERT INTO inventory."StockMovements"
                    ("Id", "ProductId", "Operation", "OperationId", "Kind", "ActorKind", "ActorUserId", "Reason",
                     "OnHandDelta", "ReservedDelta", "OnHandBefore", "ReservedBefore", "OnHandAfter", "ReservedAfter", "Revision", "OccurredAt")
                SELECT gen_random_uuid(), "Id", 'InitializeInventory:v1', "Id", 'Initialization', 'System', NULL,
                       'Product inventory initialized at zero', 0, 0, 0, 0, 0, 0, 0, "CreatedAt"
                FROM catalog."Products" ORDER BY "Id";

                CREATE FUNCTION inventory.reject_stock_movement_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'StockMovements are append-only' USING ERRCODE = '55000';
                END;
                $body$;
                CREATE TRIGGER "TR_StockMovements_AppendOnly" BEFORE UPDATE OR DELETE ON inventory."StockMovements"
                FOR EACH ROW EXECUTE FUNCTION inventory.reject_stock_movement_mutation();
                CREATE TRIGGER "TR_StockMovements_NoTruncate" BEFORE TRUNCATE ON inventory."StockMovements"
                FOR EACH STATEMENT EXECUTE FUNCTION inventory.reject_stock_movement_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Products_InitializeInventory" ON catalog."Products";
                DROP FUNCTION inventory.initialize_product_inventory();
                DROP TRIGGER "TR_StockMovements_AppendOnly" ON inventory."StockMovements";
                DROP TRIGGER "TR_StockMovements_NoTruncate" ON inventory."StockMovements";
                DROP FUNCTION inventory.reject_stock_movement_mutation();
                """);
            migrationBuilder.DropTable(
                name: "StockMovements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Balances",
                schema: "inventory");
        }
    }
}
