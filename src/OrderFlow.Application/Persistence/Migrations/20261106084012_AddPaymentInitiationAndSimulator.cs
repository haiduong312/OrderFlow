using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentInitiationAndSimulator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.EnsureSchema(
                name: "simulator");

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderRequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    NeedsAttention = table.Column<bool>(type: "boolean", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderCaptureId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ObservedState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LastObservation = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.CheckConstraint("CK_PaymentAttempts_Identity", "\"Provider\" = 'Simulator' AND \"ProviderRequestKey\" = 'payment-' || replace(\"Id\"::text,'-','')");
                    table.CheckConstraint("CK_PaymentAttempts_Lease", "(\"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL) OR (\"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"UpdatedAt\" AND \"DeliveryCount\" > 0)");
                    table.CheckConstraint("CK_PaymentAttempts_Money", "\"Currency\" = 'USD' AND \"Amount\" > 0 AND \"Amount\" <= 9999999999999999.99");
                    table.CheckConstraint("CK_PaymentAttempts_Observation", "(\"LastObservation\" IS NULL OR jsonb_typeof(\"LastObservation\") = 'object') AND (\"ObservedState\" IS DISTINCT FROM 'Succeeded' OR (\"Status\" = 'Pending' AND \"NeedsAttention\" AND \"ProviderTransactionId\" IS NOT NULL AND \"ProviderCaptureId\" IS NOT NULL))");
                    table.CheckConstraint("CK_PaymentAttempts_State", "\"Status\" IN ('Queued','Pending','Unknown','Failed') AND \"DeliveryCount\" BETWEEN 0 AND 8 AND (\"ObservedState\" IS NULL OR \"ObservedState\" IN ('Pending','Succeeded','Failed'))");
                    table.CheckConstraint("CK_PaymentAttempts_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"NextAttemptAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND \"NextAttemptAt\" >= \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderPayments",
                schema: "simulator",
                columns: table => new
                {
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CaptureId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderPayments", x => x.RequestKey);
                    table.CheckConstraint("CK_ProviderPayments_Money", "\"Currency\" = 'USD' AND \"Amount\" > 0 AND \"Amount\" <= 9999999999999999.99 AND \"RequestedAmount\" > 0 AND \"RequestedAmount\" <= 9999999999999999.99");
                    table.CheckConstraint("CK_ProviderPayments_State", "(\"State\" = 'Succeeded' AND \"CaptureId\" IS NOT NULL) OR (\"State\" IN ('Pending','Failed') AND \"CaptureId\" IS NULL)");
                    table.CheckConstraint("CK_ProviderPayments_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND \"UpdatedAt\" >= \"CreatedAt\"");
                });

            migrationBuilder.CreateTable(
                name: "ProviderScenarios",
                schema: "simulator",
                columns: table => new
                {
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    Scenario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderScenarios", x => x.RequestKey);
                });

            migrationBuilder.CreateTable(
                name: "ProviderRefunds",
                schema: "simulator",
                columns: table => new
                {
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    PaymentRequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    RefundId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CaptureId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderRefunds", x => x.RequestKey);
                    table.CheckConstraint("CK_ProviderRefunds_Money", "\"Currency\" = 'USD' AND \"Amount\" > 0 AND \"Amount\" <= 9999999999999999.99 AND isfinite(\"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_ProviderRefunds_ProviderPayments_PaymentRequestKey",
                        column: x => x.PaymentRequestKey,
                        principalSchema: "simulator",
                        principalTable: "ProviderPayments",
                        principalColumn: "RequestKey",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_NextAttemptAt_Id",
                schema: "payments",
                table: "PaymentAttempts",
                columns: new[] { "NextAttemptAt", "Id" },
                filter: "\"Status\" IN ('Queued','Pending','Unknown') AND NOT \"NeedsAttention\"");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Provider_ProviderRequestKey",
                schema: "payments",
                table: "PaymentAttempts",
                columns: new[] { "Provider", "ProviderRequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PaymentAttempts_ActiveOrder",
                schema: "payments",
                table: "PaymentAttempts",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" IN ('Queued','Pending','Unknown')");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderPayments_AttemptId",
                schema: "simulator",
                table: "ProviderPayments",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderPayments_CaptureId",
                schema: "simulator",
                table: "ProviderPayments",
                column: "CaptureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderPayments_TransactionId",
                schema: "simulator",
                table: "ProviderPayments",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderRefunds_CaptureId",
                schema: "simulator",
                table: "ProviderRefunds",
                column: "CaptureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderRefunds_PaymentRequestKey",
                schema: "simulator",
                table: "ProviderRefunds",
                column: "PaymentRequestKey");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderRefunds_RefundId",
                schema: "simulator",
                table: "ProviderRefunds",
                column: "RefundId",
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION payments.protect_attempt_snapshot() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF ROW(NEW."Id",NEW."OrderId",NEW."Provider",NEW."ProviderRequestKey",NEW."Amount",NEW."Currency",NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id",OLD."OrderId",OLD."Provider",OLD."ProviderRequestKey",OLD."Amount",OLD."Currency",OLD."CreatedAt")
                        OR NEW."DeliveryCount" < OLD."DeliveryCount" OR NEW."UpdatedAt" < OLD."UpdatedAt"
                        OR (OLD."ProviderTransactionId" IS NOT NULL AND NEW."ProviderTransactionId" IS DISTINCT FROM OLD."ProviderTransactionId")
                        OR (OLD."ProviderCaptureId" IS NOT NULL AND NEW."ProviderCaptureId" IS DISTINCT FROM OLD."ProviderCaptureId")
                        OR (OLD."ObservedState" = 'Succeeded' AND NEW."ObservedState" IS DISTINCT FROM 'Succeeded') THEN
                        RAISE EXCEPTION 'Payment identity and capture evidence are immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_PaymentAttempts_Snapshot" BEFORE UPDATE ON payments."PaymentAttempts"
                FOR EACH ROW EXECUTE FUNCTION payments.protect_attempt_snapshot();

                CREATE FUNCTION simulator.protect_payment_snapshot() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF ROW(NEW."RequestKey",NEW."AttemptId",NEW."OrderId",NEW."TransactionId",NEW."RequestedAmount",NEW."Amount",NEW."Currency",NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."RequestKey",OLD."AttemptId",OLD."OrderId",OLD."TransactionId",OLD."RequestedAmount",OLD."Amount",OLD."Currency",OLD."CreatedAt")
                        OR (OLD."State" = 'Succeeded' AND ROW(NEW."State",NEW."CaptureId",NEW."EventId",NEW."UpdatedAt")
                            IS DISTINCT FROM ROW(OLD."State",OLD."CaptureId",OLD."EventId",OLD."UpdatedAt"))
                        OR NEW."UpdatedAt" < OLD."UpdatedAt" THEN
                        RAISE EXCEPTION 'Provider identity and captured funds are immutable' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_ProviderPayments_Snapshot" BEFORE UPDATE ON simulator."ProviderPayments"
                FOR EACH ROW EXECUTE FUNCTION simulator.protect_payment_snapshot();
                CREATE TRIGGER "TR_ProviderRefunds_Immutable" BEFORE UPDATE OR DELETE ON simulator."ProviderRefunds"
                FOR EACH ROW EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_ProviderRefunds_NoTruncate" BEFORE TRUNCATE ON simulator."ProviderRefunds"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Payment/provider identities must be retained. Use a reviewed forward migration.");
    }
}
