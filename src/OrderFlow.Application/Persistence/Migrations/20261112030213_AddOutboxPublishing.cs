using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFailureAt",
                schema: "messaging",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "messaging",
                table: "OutboxMessages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                schema: "messaging",
                table: "OutboxMessages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Outbox_Failure",
                schema: "messaging",
                table: "OutboxMessages",
                sql: "\"LastFailureAt\" IS NULL OR (isfinite(\"LastFailureAt\") AND \"LastFailureAt\" >= \"OccurredAt\")");
            migrationBuilder.Sql("""
                CREATE FUNCTION messaging.protect_outbox_event() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Outbox identities must be retained' USING ERRCODE='55000'; END IF;
                    IF ROW(NEW."EventId",NEW."EventType",NEW."SchemaVersion",NEW."Producer",NEW."AggregateType",NEW."AggregateId",NEW."AggregateVersion",NEW."CorrelationId",NEW."CausationId",NEW."Payload",NEW."OccurredAt",NEW."TraceParent",NEW."TraceState")
                        IS DISTINCT FROM ROW(OLD."EventId",OLD."EventType",OLD."SchemaVersion",OLD."Producer",OLD."AggregateType",OLD."AggregateId",OLD."AggregateVersion",OLD."CorrelationId",OLD."CausationId",OLD."Payload",OLD."OccurredAt",OLD."TraceParent",OLD."TraceState")
                        OR NEW."AttemptCount" < OLD."AttemptCount"
                        OR (OLD."Status"='Published' AND NEW IS DISTINCT FROM OLD) THEN
                        RAISE EXCEPTION 'Outbox envelope and published facts cannot regress' USING ERRCODE='55000';
                    END IF;
                    RETURN NEW;
                END; $body$;
                CREATE TRIGGER "TR_Outbox_ProtectEvent" BEFORE UPDATE OR DELETE ON messaging."OutboxMessages"
                FOR EACH ROW EXECUTE FUNCTION messaging.protect_outbox_event();
                CREATE TRIGGER "TR_Outbox_NoTruncate" BEFORE TRUNCATE ON messaging."OutboxMessages"
                FOR EACH STATEMENT EXECUTE FUNCTION ordering.reject_snapshot_mutation();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Outbox identity, trace and publication evidence must be retained. Use a reviewed forward migration.");
    }
}
