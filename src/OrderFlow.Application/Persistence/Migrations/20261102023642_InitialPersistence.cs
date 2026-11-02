using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OrderFlow.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "messaging",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Producer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CausationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                    table.CheckConstraint("CK_Outbox_Attempts", "\"AttemptCount\" >= 0 AND isfinite(\"OccurredAt\") AND isfinite(\"NextAttemptAt\") AND \"NextAttemptAt\" >= \"OccurredAt\"");
                    table.CheckConstraint("CK_Outbox_Lease", "(\"Status\" = 'InFlight' AND \"LeaseOwner\" IS NOT NULL AND length(btrim(\"LeaseOwner\")) > 0 AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"OccurredAt\") OR (\"Status\" <> 'InFlight' AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_Outbox_Metadata", "length(btrim(\"EventType\")) > 0 AND length(btrim(\"Producer\")) > 0 AND length(btrim(\"AggregateType\")) > 0 AND \"SchemaVersion\" > 0 AND \"AggregateVersion\" > 0 AND jsonb_typeof(\"Payload\") = 'object'");
                    table.CheckConstraint("CK_Outbox_Published", "(\"Status\" = 'Published' AND \"PublishedAt\" IS NOT NULL AND isfinite(\"PublishedAt\") AND \"PublishedAt\" >= \"OccurredAt\") OR (\"Status\" <> 'Published' AND \"PublishedAt\" IS NULL)");
                    table.CheckConstraint("CK_Outbox_Status", "\"Status\" IN ('Pending','InFlight','Published','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                schema: "messaging",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.ConsumerName, x.MessageId });
                    table.CheckConstraint("CK_Inbox_Attempts", "\"AttemptCount\" >= 0 AND isfinite(\"ReceivedAt\") AND isfinite(\"NextAttemptAt\") AND \"NextAttemptAt\" >= \"ReceivedAt\"");
                    table.CheckConstraint("CK_Inbox_Lease", "(\"Status\" = 'InFlight' AND \"LeaseOwner\" IS NOT NULL AND length(btrim(\"LeaseOwner\")) > 0 AND \"LeaseToken\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND isfinite(\"LeaseExpiresAt\") AND \"LeaseExpiresAt\" > \"ReceivedAt\") OR (\"Status\" <> 'InFlight' AND \"LeaseOwner\" IS NULL AND \"LeaseToken\" IS NULL AND \"LeaseExpiresAt\" IS NULL)");
                    table.CheckConstraint("CK_Inbox_Metadata", "length(btrim(\"ConsumerName\")) > 0 AND length(btrim(\"MessageType\")) > 0 AND \"SchemaVersion\" > 0 AND octet_length(\"PayloadHash\") = 32 AND jsonb_typeof(\"Payload\") = 'object'");
                    table.CheckConstraint("CK_Inbox_Processed", "(\"Status\" = 'Processed' AND \"ProcessedAt\" IS NOT NULL AND isfinite(\"ProcessedAt\") AND \"ProcessedAt\" >= \"ReceivedAt\") OR (\"Status\" <> 'Processed' AND \"ProcessedAt\" IS NULL)");
                    table.CheckConstraint("CK_Inbox_Status", "\"Status\" IN ('Pending','InFlight','Processed','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_CreatedAt", "isfinite(\"CreatedAt\")");
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "C"),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                    CanonicalRequest = table.Column<string>(type: "text", nullable: false),
                    RequestHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResponseLocation = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.CheckConstraint("CK_Idempotency_Key", "\"Key\" ~ '^[!-~]{1,128}$'");
                    table.CheckConstraint("CK_Idempotency_Request", "length(btrim(\"Operation\")) > 0 AND length(\"CanonicalRequest\") > 0 AND octet_length(\"RequestHash\") = 32");
                    table.CheckConstraint("CK_Idempotency_Snapshot", "(\"Status\" = 'InProgress' AND \"CompletedAt\" IS NULL AND \"ResponseStatusCode\" IS NULL AND \"ResponseContentType\" IS NULL AND \"ResponseBody\" IS NULL AND \"ResponseLocation\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL AND \"ResponseStatusCode\" IS NOT NULL AND \"ResponseStatusCode\" BETWEEN 200 AND 299 AND \"ResponseContentType\" IS NOT NULL AND length(btrim(\"ResponseContentType\")) > 0 AND \"ResponseBody\" IS NOT NULL)");
                    table.CheckConstraint("CK_Idempotency_Times", "isfinite(\"CreatedAt\") AND (\"CompletedAt\" IS NULL OR (isfinite(\"CompletedAt\") AND \"CompletedAt\" >= \"CreatedAt\"))");
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Idempotency_User_Operation_Key",
                schema: "messaging",
                table: "IdempotencyRecords",
                columns: new[] { "UserId", "Operation", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Due",
                schema: "messaging",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptAt", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ExpiredLease",
                schema: "messaging",
                table: "OutboxMessages",
                column: "LeaseExpiresAt",
                filter: "\"Status\" = 'InFlight'");

            migrationBuilder.CreateIndex(
                name: "UX_Outbox_ProducerEffect",
                schema: "messaging",
                table: "OutboxMessages",
                columns: new[] { "Producer", "AggregateType", "AggregateId", "AggregateVersion", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_Due",
                schema: "messaging",
                table: "ProcessedMessages",
                columns: new[] { "Status", "NextAttemptAt", "ConsumerName", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_ExpiredLease",
                schema: "messaging",
                table: "ProcessedMessages",
                column: "LeaseExpiresAt",
                filter: "\"Status\" = 'InFlight'");

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "identity",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                schema: "identity",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                schema: "identity",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "identity",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);

            // A completed replay snapshot must not be rewritten by a racing process or raw SQL.
            migrationBuilder.Sql("""
                CREATE FUNCTION messaging."ProtectIdempotencyRecord"() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF (OLD."Status" = 'Completed' AND NEW IS DISTINCT FROM OLD)
                       OR ROW(NEW."Id", NEW."UserId", NEW."Operation", NEW."Key", NEW."CanonicalRequest", NEW."RequestHash", NEW."CreatedAt")
                          IS DISTINCT FROM ROW(OLD."Id", OLD."UserId", OLD."Operation", OLD."Key", OLD."CanonicalRequest", OLD."RequestHash", OLD."CreatedAt") THEN
                        RAISE EXCEPTION 'Idempotency identity/request and completed snapshots are immutable'
                            USING ERRCODE = '23514', CONSTRAINT = 'CK_Idempotency_Immutable';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER "TR_Idempotency_Immutable" BEFORE UPDATE ON messaging."IdempotencyRecords"
                    FOR EACH ROW EXECUTE FUNCTION messaging."ProtectIdempotencyRecord"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "messaging");

            migrationBuilder.Sql("DROP FUNCTION messaging.\"ProtectIdempotencyRecord\"()");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "ProcessedMessages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "RoleClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserClaims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserLogins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "identity");
        }
    }
}
