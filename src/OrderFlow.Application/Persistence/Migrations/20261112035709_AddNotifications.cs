using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notification_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SourceEventId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Notification_Times", "isfinite(\"CreatedAt\") AND (\"ReadAt\" IS NULL OR (isfinite(\"ReadAt\") AND \"ReadAt\" >= \"CreatedAt\"))");
                    table.CheckConstraint("CK_Notification_Type", "\"Type\" IN ('ordering.order-created','ordering.order-confirmed','ordering.order-cancelled','ordering.order-expired','ordering.order-processing','ordering.order-shipped','ordering.order-completed')");
                    table.ForeignKey(
                        name: "FK_Notifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "ordering",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_User_Created",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_User_Unread",
                schema: "notifications",
                table: "Notifications",
                column: "RecipientUserId",
                filter: "\"ReadAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrderId",
                schema: "notifications",
                table: "Notifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "UX_Notification_Source_Type_Recipient",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "SourceEventId", "Type", "RecipientUserId" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION notifications.protect_notification() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Notification identities must be retained' USING ERRCODE='55000'; END IF;
                    IF TG_OP='UPDATE' AND (
                        ROW(NEW."Id",NEW."RecipientUserId",NEW."SourceEventId",NEW."Type",NEW."OrderId",NEW."CreatedAt")
                        IS DISTINCT FROM ROW(OLD."Id",OLD."RecipientUserId",OLD."SourceEventId",OLD."Type",OLD."OrderId",OLD."CreatedAt")
                        OR (OLD."ReadAt" IS NOT NULL AND NEW."ReadAt" IS DISTINCT FROM OLD."ReadAt")) THEN
                        RAISE EXCEPTION 'Notification identity and read receipt cannot regress' USING ERRCODE='55000';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM ordering."Orders" WHERE "Id"=NEW."OrderId" AND "UserId"=NEW."RecipientUserId") THEN
                        RAISE EXCEPTION 'Notification recipient must own its order' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_Notification_Protect" BEFORE INSERT OR UPDATE OR DELETE ON notifications."Notifications"
                FOR EACH ROW EXECUTE FUNCTION notifications.protect_notification();
                CREATE TRIGGER "TR_Notification_NoTruncate" BEFORE TRUNCATE ON notifications."Notifications"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();

                CREATE FUNCTION notifications.protect_handled_inbox() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF OLD."ConsumerName"='notifications.orders.v1' AND OLD."Status"='Processed'
                        AND (TG_OP='DELETE' OR NEW IS DISTINCT FROM OLD) THEN
                        RAISE EXCEPTION 'Handled notification inbox identities must be retained' USING ERRCODE='55000';
                    END IF;
                    IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_NotificationInbox_Protect" BEFORE UPDATE OR DELETE ON messaging."ProcessedMessages"
                FOR EACH ROW EXECUTE FUNCTION notifications.protect_handled_inbox();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Notification identities/read receipts/inbox evidence must be retained. Use a reviewed forward migration.");
    }
}
