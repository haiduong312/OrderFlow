using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Refunds_Intention",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentCaptures_Facts",
                schema: "payments",
                table: "PaymentCaptures");

            migrationBuilder.AddColumn<int>(
                name: "DeliveryCount",
                schema: "payments",
                table: "Refunds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                schema: "payments",
                table: "Refunds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                schema: "payments",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                schema: "payments",
                table: "Refunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "payments",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "ProviderRefundId",
                schema: "payments",
                table: "Refunds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SucceededAt",
                schema: "payments",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "payments",
                table: "Refunds",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ProviderOccurredAt",
                schema: "payments",
                table: "PaymentCaptures",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProviderEventId",
                schema: "payments",
                table: "PaymentCaptures",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "payments",
                table: "PaymentCaptures",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Callback");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciledAt",
                schema: "payments",
                table: "PaymentAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderRefundScenarios",
                schema: "simulator",
                columns: table => new
                {
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    Scenario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderRefundScenarios", x => x.RequestKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_Due",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "NextAttemptAt", "Id" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_Provider_ProviderRefundId",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "Provider", "ProviderRefundId" },
                unique: true,
                filter: "\"ProviderRefundId\" IS NOT NULL");

            // Preserve existing compensation identities and amounts while enabling delivery metadata.
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Refunds_Immutable" ON payments."Refunds";
                UPDATE payments."Refunds" SET "Status"='Pending', "UpdatedAt"="CreatedAt", "NextAttemptAt"="CreatedAt";
                ALTER TABLE payments."Refunds" ALTER COLUMN "UpdatedAt" DROP DEFAULT;
                ALTER TABLE payments."Refunds" ALTER COLUMN "NextAttemptAt" DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_Delivery",
                schema: "payments",
                table: "Refunds",
                sql: "\"DeliveryCount\" BETWEEN 0 AND 8 AND isfinite(\"UpdatedAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND isfinite(\"NextAttemptAt\") AND \"NextAttemptAt\" >= \"CreatedAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_Intention",
                schema: "payments",
                table: "Refunds",
                sql: "\"Provider\" = 'Simulator' AND \"Purpose\" = 'FullCompensation' AND \"Status\" IN ('Pending','Succeeded','NeedsReview') AND \"Currency\" = 'USD' AND \"Amount\" > 0 AND isfinite(\"CreatedAt\") AND length(\"Reason\") > 0 AND \"ProviderRefundKey\" = 'refund-' || replace(\"Id\"::text,'-','')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_Lease",
                schema: "payments",
                table: "Refunds",
                sql: "(\"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL) OR (\"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"UpdatedAt\" AND \"DeliveryCount\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Refunds_Outcome",
                schema: "payments",
                table: "Refunds",
                sql: "(\"Status\" = 'Succeeded' AND \"ProviderRefundId\" IS NOT NULL AND length(\"ProviderRefundId\") > 0 AND \"SucceededAt\" IS NOT NULL AND isfinite(\"SucceededAt\") AND \"SucceededAt\" >= \"CreatedAt\" AND \"LeaseToken\" IS NULL AND \"LastErrorCode\" IS NULL) OR (\"Status\" IN ('Pending','NeedsReview') AND \"ProviderRefundId\" IS NULL AND \"SucceededAt\" IS NULL AND (\"Status\" <> 'NeedsReview' OR \"LastErrorCode\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentCaptures_Facts",
                schema: "payments",
                table: "PaymentCaptures",
                sql: "\"Provider\" = 'Simulator' AND length(\"ProviderCaptureId\") > 0 AND length(\"ProviderTransactionId\") > 0 AND \"Amount\" > 0 AND \"Currency\" = 'USD' AND (\"ProviderOccurredAt\" IS NULL OR isfinite(\"ProviderOccurredAt\")) AND isfinite(\"RecordedAt\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentCaptures_Source",
                schema: "payments",
                table: "PaymentCaptures",
                sql: "(\"Source\" = 'Callback' AND \"ProviderEventId\" IS NOT NULL AND \"ProviderOccurredAt\" IS NOT NULL) OR (\"Source\" = 'ProviderLookup' AND \"ProviderEventId\" IS NULL AND \"ProviderOccurredAt\" IS NULL)");
                    migrationBuilder.Sql("""
                CREATE FUNCTION payments.protect_refund_snapshot() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Refund identities must be retained' USING ERRCODE='55000'; END IF;
                    IF ROW(NEW."Id",NEW."CaptureId",NEW."Provider",NEW."ProviderCaptureId",NEW."Purpose",NEW."ProviderRefundKey",NEW."Amount",NEW."Currency",NEW."Reason",NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id",OLD."CaptureId",OLD."Provider",OLD."ProviderCaptureId",OLD."Purpose",OLD."ProviderRefundKey",OLD."Amount",OLD."Currency",OLD."Reason",OLD."CreatedAt")
                        OR NEW."DeliveryCount" < OLD."DeliveryCount" OR NEW."UpdatedAt" < OLD."UpdatedAt"
                        OR (OLD."Status"='Succeeded' AND NEW IS DISTINCT FROM OLD)
                        OR (OLD."Status"='NeedsReview' AND OLD."LeaseToken" IS NULL AND NEW IS DISTINCT FROM OLD) THEN
                        RAISE EXCEPTION 'Refund snapshot, success and manual-review facts cannot regress' USING ERRCODE='55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_Refunds_ProtectSnapshot" BEFORE UPDATE OR DELETE ON payments."Refunds"
                FOR EACH ROW EXECUTE FUNCTION payments.protect_refund_snapshot();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Refund execution identities and lookup evidence must be retained. Use a reviewed forward migration.");
    }
}
