using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailDeliveries",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FromEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Template = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TextBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptsInCycle = table.Column<int>(type: "integer", nullable: false),
                    ReviewRetryCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OutcomeUncertain = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveries", x => x.Id);
                    table.CheckConstraint("CK_Email_Counts", "\"AttemptCount\">=0 AND \"AttemptsInCycle\" BETWEEN 0 AND 8 AND \"AttemptsInCycle\"<=\"AttemptCount\" AND \"ReviewRetryCount\">=0");
                    table.CheckConstraint("CK_Email_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SourceEventId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Template\"='order-confirmation:v1' AND length(\"ProviderKey\")>0 AND length(\"MessageId\")>0 AND octet_length(\"RequestHash\")=32");
                    table.CheckConstraint("CK_Email_Lease", "(\"Status\"='InFlight' AND \"LeaseOwner\" IS NOT NULL AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\">\"CreatedAt\" AND \"OutcomeUncertain\") OR (\"Status\"<>'InFlight' AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_Email_Sent", "(\"Status\"='Sent' AND \"SentAt\" IS NOT NULL AND isfinite(\"SentAt\") AND \"SentAt\">=\"CreatedAt\" AND NOT \"OutcomeUncertain\") OR (\"Status\"<>'Sent' AND \"SentAt\" IS NULL)");
                    table.CheckConstraint("CK_Email_Status", "\"Status\" IN ('Pending','InFlight','Sent','NeedsReview')");
                    table.CheckConstraint("CK_Email_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"NextAttemptAt\") AND \"UpdatedAt\">=\"CreatedAt\" AND \"NextAttemptAt\">=\"CreatedAt\" AND (\"LastFailureAt\" IS NULL OR (isfinite(\"LastFailureAt\") AND \"LastFailureAt\">=\"CreatedAt\"))");
                    table.ForeignKey(
                        name: "FK_EmailDeliveries_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailDeliveries_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailDeliveryRetries",
                schema: "notifications",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CycleNumber = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AcknowledgeDuplicateRisk = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryRetries", x => x.OperationId);
                    table.CheckConstraint("CK_EmailRetry_Metadata", "\"CycleNumber\">0 AND length(btrim(\"Reason\")) BETWEEN 1 AND 300 AND isfinite(\"RequestedAt\") AND \"OperationId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_EmailDeliveryRetries_EmailDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalSchema: "notifications",
                        principalTable: "EmailDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryRetries_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Email_Due",
                schema: "notifications",
                table: "EmailDeliveries",
                columns: new[] { "Status", "NextAttemptAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Email_Lease",
                schema: "notifications",
                table: "EmailDeliveries",
                column: "LeaseExpiresAt",
                filter: "\"Status\"='InFlight'");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveries_Provider_ProviderKey",
                schema: "notifications",
                table: "EmailDeliveries",
                columns: new[] { "Provider", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveries_RecipientUserId",
                schema: "notifications",
                table: "EmailDeliveries",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "UX_Email_Order_Template",
                schema: "notifications",
                table: "EmailDeliveries",
                columns: new[] { "OrderId", "Template" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Email_Source_Template_Recipient",
                schema: "notifications",
                table: "EmailDeliveries",
                columns: new[] { "SourceEventId", "Template", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryRetries_ActorUserId",
                schema: "notifications",
                table: "EmailDeliveryRetries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryRetries_DeliveryId_CycleNumber",
                schema: "notifications",
                table: "EmailDeliveryRetries",
                columns: new[] { "DeliveryId", "CycleNumber" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION notifications.protect_email_delivery() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Email delivery identities must be retained' USING ERRCODE='55000'; END IF;
                    IF TG_OP='INSERT' THEN
                        IF NOT EXISTS (SELECT 1 FROM ordering."Orders" o WHERE o."Id"=NEW."OrderId" AND o."UserId"=NEW."RecipientUserId")
                            OR NOT EXISTS (SELECT 1 FROM ordering."OrderStatusHistory" h WHERE h."OrderId"=NEW."OrderId" AND h."Status"='Confirmed') THEN
                            RAISE EXCEPTION 'Confirmation email must reference its owner and confirmation history' USING ERRCODE='23514';
                        END IF;
                        RETURN NEW;
                    END IF;
                    IF ROW(NEW."Id",NEW."SourceEventId",NEW."OrderId",NEW."RecipientUserId",NEW."RecipientEmail",NEW."FromEmail",NEW."Template",NEW."Subject",NEW."TextBody",NEW."Provider",NEW."ProviderKey",NEW."MessageId",NEW."RequestHash",NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id",OLD."SourceEventId",OLD."OrderId",OLD."RecipientUserId",OLD."RecipientEmail",OLD."FromEmail",OLD."Template",OLD."Subject",OLD."TextBody",OLD."Provider",OLD."ProviderKey",OLD."MessageId",OLD."RequestHash",OLD."CreatedAt")
                        OR NEW."AttemptCount"<OLD."AttemptCount" OR NEW."ReviewRetryCount"<OLD."ReviewRetryCount"
                        OR (OLD."Status"='Sent' AND NEW IS DISTINCT FROM OLD) THEN
                        RAISE EXCEPTION 'Email identity, counters and sent facts cannot regress' USING ERRCODE='55000';
                    END IF;
                    IF NEW."ReviewRetryCount"<>OLD."ReviewRetryCount" OR NEW."AttemptsInCycle"<OLD."AttemptsInCycle" OR (OLD."Status"='NeedsReview' AND NEW."Status"<>'NeedsReview') THEN
                        IF NOT (OLD."Status"='NeedsReview' AND NEW."Status"='Pending' AND NEW."AttemptsInCycle"=0
                            AND NEW."ReviewRetryCount"=OLD."ReviewRetryCount"+1
                            AND EXISTS (SELECT 1 FROM notifications."EmailDeliveryRetries" r WHERE r."DeliveryId"=NEW."Id" AND r."CycleNumber"=NEW."ReviewRetryCount")) THEN
                            RAISE EXCEPTION 'Email retry cycle requires its durable operator audit' USING ERRCODE='55000';
                        END IF;
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_Email_Protect" BEFORE INSERT OR UPDATE OR DELETE ON notifications."EmailDeliveries"
                FOR EACH ROW EXECUTE FUNCTION notifications.protect_email_delivery();
                CREATE TRIGGER "TR_Email_NoTruncate" BEFORE TRUNCATE ON notifications."EmailDeliveries"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_EmailRetry_AppendOnly" BEFORE UPDATE OR DELETE ON notifications."EmailDeliveryRetries"
                FOR EACH ROW EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                CREATE TRIGGER "TR_EmailRetry_NoTruncate" BEFORE TRUNCATE ON notifications."EmailDeliveryRetries"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Email identities, snapshots, send evidence and retry audits must be retained. Use a reviewed forward migration.");
    }
}
