using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignedPaymentSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAttempts_Observation",
                schema: "payments",
                table: "PaymentAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAttempts_State",
                schema: "payments",
                table: "PaymentAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderStatusHistory_Actor",
                schema: "ordering",
                table: "OrderStatusHistory");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsumedAt",
                schema: "inventory",
                table: "InventoryReservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                schema: "payments",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    ProviderEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawBody = table.Column<byte[]>(type: "bytea", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ReferencedOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecisionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => new { x.Provider, x.ProviderEventId });
                    table.CheckConstraint("CK_PaymentEvents_Payload", "\"Provider\" = 'Simulator' AND octet_length(\"RawBody\") BETWEEN 1 AND 65536 AND octet_length(\"PayloadHash\") = 32");
                    table.CheckConstraint("CK_PaymentEvents_State", "(\"Status\" = 'Pending' AND \"DecisionAt\" IS NULL AND \"ErrorCode\" IS NULL) OR (\"Status\" = 'Processed' AND \"DecisionAt\" IS NOT NULL AND \"ErrorCode\" IS NULL) OR (\"Status\" = 'Quarantined' AND \"DecisionAt\" IS NOT NULL AND \"ErrorCode\" IS NOT NULL)");
                    table.CheckConstraint("CK_PaymentEvents_Time", "isfinite(\"OccurredAt\") AND isfinite(\"ReceivedAt\") AND (\"DecisionAt\" IS NULL OR (isfinite(\"DecisionAt\") AND \"DecisionAt\" >= \"ReceivedAt\"))");
                });

            migrationBuilder.CreateTable(
                name: "PaymentCaptures",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    ProviderCaptureId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    ProviderTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Allocated = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCaptures", x => x.Id);
                    table.CheckConstraint("CK_PaymentCaptures_Facts", "\"Provider\" = 'Simulator' AND length(\"ProviderCaptureId\") > 0 AND length(\"ProviderTransactionId\") > 0 AND \"Amount\" > 0 AND \"Currency\" = 'USD' AND isfinite(\"ProviderOccurredAt\") AND isfinite(\"RecordedAt\")");
                    table.ForeignKey(
                        name: "FK_PaymentCaptures_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentCaptures_PaymentAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalSchema: "payments",
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentCaptures_PaymentEvents_Provider_ProviderEventId",
                        columns: x => new { x.Provider, x.ProviderEventId },
                        principalSchema: "payments",
                        principalTable: "PaymentEvents",
                        principalColumns: new[] { "Provider", "ProviderEventId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaptureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    ProviderCaptureId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderRefundKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Intention", "\"Provider\" = 'Simulator' AND \"Purpose\" = 'FullCompensation' AND \"Status\" = 'RefundPending' AND \"Currency\" = 'USD' AND \"Amount\" > 0 AND isfinite(\"CreatedAt\") AND length(\"Reason\") > 0 AND \"ProviderRefundKey\" = 'refund-' || replace(\"Id\"::text,'-','')");
                    table.ForeignKey(
                        name: "FK_Refunds_PaymentCaptures_CaptureId",
                        column: x => x.CaptureId,
                        principalSchema: "payments",
                        principalTable: "PaymentCaptures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_Kind",
                schema: "inventory",
                table: "StockMovements",
                sql: "(\"Kind\" = 'Initialization' AND \"Operation\" = 'InitializeInventory:v1'\n AND \"OperationId\" = \"ProductId\" AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL\n AND \"Revision\" = 0 AND \"OnHandBefore\" = 0 AND \"OnHandAfter\" = 0\n AND \"ReservedBefore\" = 0 AND \"ReservedAfter\" = 0 AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" = 0\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" IN ('Receipt','Adjustment') AND \"Operation\" = 'ChangeStock:v1'\n AND \"ActorKind\" = 'Admin' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"ReservedDelta\" = 0 AND \"OnHandDelta\" <> 0\n AND (\"Kind\" <> 'Receipt' OR \"OnHandDelta\" > 0)\n AND \"OrderId\" IS NULL AND \"ReservationId\" IS NULL)\nOR\n(\"Kind\" = 'Reservation' AND \"Operation\" = 'ReserveStock:v1'\n AND \"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN 1 AND 1000)\nOR\n(\"Kind\" = 'Release' AND \"Operation\" = 'ReleaseStock:v1'\n AND ((\"ActorKind\" = 'Customer' AND \"ActorUserId\" IS NOT NULL)\n      OR (\"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL AND \"Reason\" = 'Reservation released: Expired')) AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"OnHandDelta\" = 0 AND \"ReservedDelta\" BETWEEN -1000 AND -1)\nOR\n(\"Kind\" = 'Consumption' AND \"Operation\" = 'ConsumeStock:v1'\n AND \"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL AND \"Revision\" > 0\n AND \"OrderId\" IS NOT NULL AND \"ReservationId\" IS NOT NULL AND \"OperationId\" = \"ReservationId\"\n AND \"ReservedDelta\" BETWEEN -1000 AND -1 AND \"OnHandDelta\" = \"ReservedDelta\")");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Provider_ProviderTransactionId",
                schema: "payments",
                table: "PaymentAttempts",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAttempts_Captured",
                schema: "payments",
                table: "PaymentAttempts",
                sql: "\"Status\" <> 'Captured' OR (\"ObservedState\" IS NOT DISTINCT FROM 'Succeeded' AND \"ProviderCaptureId\" IS NOT NULL AND \"ProviderTransactionId\" IS NOT NULL AND \"LeaseToken\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAttempts_Observation",
                schema: "payments",
                table: "PaymentAttempts",
                sql: "(\"LastObservation\" IS NULL OR jsonb_typeof(\"LastObservation\") = 'object') AND (\"ObservedState\" IS DISTINCT FROM 'Succeeded' OR ((\"Status\" = 'Pending' AND \"NeedsAttention\" OR \"Status\" = 'Captured') AND \"ProviderTransactionId\" IS NOT NULL AND \"ProviderCaptureId\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAttempts_State",
                schema: "payments",
                table: "PaymentAttempts",
                sql: "\"Status\" IN ('Queued','Pending','Unknown','Failed','Captured') AND \"DeliveryCount\" BETWEEN 0 AND 8 AND (\"ObservedState\" IS NULL OR \"ObservedState\" IN ('Pending','Succeeded','Failed'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderStatusHistory_Actor",
                schema: "ordering",
                table: "OrderStatusHistory",
                sql: "((\"ActorKind\" IN ('Customer','Admin') AND \"ActorUserId\" IS NOT NULL) OR (\"ActorKind\" = 'System' AND \"ActorUserId\" IS NULL AND \"FromStatus\" IS NOT NULL AND \"FromStatus\" = 'PendingPayment' AND \"Status\" IN ('Expired','Confirmed'))) AND (\"FromStatus\" IS NULL OR \"FromStatus\" IN ('PendingPayment','Confirmed','Processing','Shipped','Completed','Cancelled','Expired'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Consumption",
                schema: "inventory",
                table: "InventoryReservations",
                sql: "(\"Status\" = 'Consumed' AND \"ConsumedAt\" IS NOT NULL AND isfinite(\"ConsumedAt\") AND \"ConsumedAt\" >= \"CreatedAt\" AND \"ConsumedAt\" < \"ExpiresAt\") OR (\"Status\" <> 'Consumed' AND \"ConsumedAt\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCaptures_AttemptId",
                schema: "payments",
                table: "PaymentCaptures",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCaptures_Provider_ProviderCaptureId",
                schema: "payments",
                table: "PaymentCaptures",
                columns: new[] { "Provider", "ProviderCaptureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCaptures_Provider_ProviderEventId",
                schema: "payments",
                table: "PaymentCaptures",
                columns: new[] { "Provider", "ProviderEventId" });

            migrationBuilder.CreateIndex(
                name: "UX_PaymentCaptures_AllocatedOrder",
                schema: "payments",
                table: "PaymentCaptures",
                column: "OrderId",
                unique: true,
                filter: "\"Allocated\"");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ReceivedAt_ProviderEventId",
                schema: "payments",
                table: "PaymentEvents",
                columns: new[] { "ReceivedAt", "ProviderEventId" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_CaptureId",
                schema: "payments",
                table: "Refunds",
                column: "CaptureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_Provider_ProviderCaptureId_Purpose",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "Provider", "ProviderCaptureId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_ProviderRefundKey",
                schema: "payments",
                table: "Refunds",
                column: "ProviderRefundKey",
                unique: true);
                    // Keep authenticated evidence, capture allocation and compensation identities durable.
            migrationBuilder.Sql("""
                CREATE FUNCTION payments.protect_event_receipt() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Payment receipts must be retained' USING ERRCODE = '55000'; END IF;
                    IF ROW(NEW."Provider",NEW."ProviderEventId",NEW."RawBody",NEW."PayloadHash",NEW."ReferencedOrderId",NEW."OccurredAt",NEW."ReceivedAt")
                        IS DISTINCT FROM ROW(OLD."Provider",OLD."ProviderEventId",OLD."RawBody",OLD."PayloadHash",OLD."ReferencedOrderId",OLD."OccurredAt",OLD."ReceivedAt")
                        OR (OLD."Status" <> 'Pending' AND ROW(NEW."Status",NEW."DecisionAt",NEW."ErrorCode")
                            IS DISTINCT FROM ROW(OLD."Status",OLD."DecisionAt",OLD."ErrorCode")) THEN
                        RAISE EXCEPTION 'Authenticated receipt and terminal processing facts are immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_PaymentEvents_Receipt" BEFORE UPDATE OR DELETE ON payments."PaymentEvents"
                FOR EACH ROW EXECUTE FUNCTION payments.protect_event_receipt();
                CREATE TRIGGER "TR_PaymentEvents_NoTruncate" BEFORE TRUNCATE ON payments."PaymentEvents"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_PaymentCaptures_Immutable" BEFORE UPDATE OR DELETE ON payments."PaymentCaptures"
                FOR EACH ROW EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_PaymentCaptures_NoTruncate" BEFORE TRUNCATE ON payments."PaymentCaptures"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_Refunds_Immutable" BEFORE UPDATE OR DELETE ON payments."Refunds"
                FOR EACH ROW EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_Refunds_NoTruncate" BEFORE TRUNCATE ON payments."Refunds"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();

                CREATE FUNCTION payments.protect_terminal_payment() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF (OLD."Status" = 'Captured' AND NEW."Status" <> 'Captured')
                        OR (OLD."Status" = 'Failed' AND NEW."Status" NOT IN ('Failed','Captured')) THEN
                        RAISE EXCEPTION 'Terminal payment facts cannot regress' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_PaymentAttempts_Terminal" BEFORE UPDATE ON payments."PaymentAttempts"
                FOR EACH ROW EXECUTE FUNCTION payments.protect_terminal_payment();

                CREATE OR REPLACE FUNCTION inventory.protect_reservation_snapshot() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF ROW(NEW."Id",NEW."OrderId",NEW."ProductId",NEW."Quantity",NEW."CreatedAt",NEW."ExpiresAt")
                        IS DISTINCT FROM ROW(OLD."Id",OLD."OrderId",OLD."ProductId",OLD."Quantity",OLD."CreatedAt",OLD."ExpiresAt")
                        OR (OLD."Status" <> 'Active' AND ROW(NEW."Status",NEW."ReleasedAt",NEW."ReleaseReason",NEW."ConsumedAt")
                            IS DISTINCT FROM ROW(OLD."Status",OLD."ReleasedAt",OLD."ReleaseReason",OLD."ConsumedAt")) THEN
                        RAISE EXCEPTION 'Reservation facts and terminal disposition are immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $body$;

                CREATE FUNCTION payments.check_capture_disposition() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM payments."PaymentAttempts" a
                        WHERE a."Id"=NEW."AttemptId" AND a."OrderId"=NEW."OrderId" AND a."Provider"=NEW."Provider"
                          AND a."ProviderTransactionId"=NEW."ProviderTransactionId" AND a."Status"='Captured') THEN
                        RAISE EXCEPTION 'Capture must belong to its captured attempt' USING ERRCODE = '23514';
                    END IF;
                    IF NEW."Allocated" THEN
                        IF NOT EXISTS (SELECT 1 FROM ordering."Orders" o WHERE o."Id"=NEW."OrderId"
                            AND o."Status" IN ('Confirmed','Processing','Shipped','Completed') AND o."Total"=NEW."Amount" AND o."Currency"=NEW."Currency")
                            OR EXISTS (SELECT 1 FROM payments."Refunds" r WHERE r."CaptureId"=NEW."Id") THEN
                            RAISE EXCEPTION 'Allocated capture must confirm the matching order' USING ERRCODE = '23514';
                        END IF;
                    ELSIF NOT EXISTS (SELECT 1 FROM payments."Refunds" r WHERE r."CaptureId"=NEW."Id"
                        AND r."Provider"=NEW."Provider" AND r."ProviderCaptureId"=NEW."ProviderCaptureId"
                        AND r."Amount"=NEW."Amount" AND r."Currency"=NEW."Currency") THEN
                        RAISE EXCEPTION 'Unallocated capture requires full compensation intention' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER "TR_PaymentCaptures_Disposition" AFTER INSERT ON payments."PaymentCaptures"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION payments.check_capture_disposition();
                CREATE FUNCTION payments.check_refund_capture() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM payments."PaymentCaptures" c WHERE c."Id"=NEW."CaptureId" AND NOT c."Allocated"
                        AND c."Provider"=NEW."Provider" AND c."ProviderCaptureId"=NEW."ProviderCaptureId"
                        AND c."Amount"=NEW."Amount" AND c."Currency"=NEW."Currency") THEN
                        RAISE EXCEPTION 'Compensation must match unallocated captured funds' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE CONSTRAINT TRIGGER "TR_Refunds_Capture" AFTER INSERT ON payments."Refunds"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION payments.check_refund_capture();

                CREATE FUNCTION ordering.protect_terminal_order() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF OLD."Status" IN ('Cancelled','Expired','Completed') AND NEW."Status" IS DISTINCT FROM OLD."Status" THEN
                        RAISE EXCEPTION 'A terminal order cannot resurrect' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_Orders_Terminal" BEFORE UPDATE ON ordering."Orders"
                FOR EACH ROW EXECUTE FUNCTION ordering.protect_terminal_order();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Authenticated payment, capture and refund identities must be retained. Use a reviewed forward migration.");
    }
}
