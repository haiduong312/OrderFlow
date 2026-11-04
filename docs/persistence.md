# PostgreSQL persistence — milestones 03/07

One scoped OrderFlowDbContext contains Identity and the three durable reliability stores. Milestone 07 adds Catalog Products/Categories with explicit mappings/migration. No order/inventory/payment entities, broker, dispatcher, lease-claim worker or automatic migration/seed exists. Milestone 04 uses the existing Identity storage for [browser authentication and opt-in development accounts](authentication.md); no additional migration is needed. See [ADR-009](adr/0009-postgresql-reliability-foundations.md), [architecture](architecture.md), and [implementation status](implementation-status.md).

## Local PostgreSQL only

Use Docker Engine with Compose v2+ (the `!reset` verification workaround described in status is not needed for a normal local daemon). The committed [compose.yaml](../compose.yaml) has a single `dev` profile service: PostgreSQL **18.6-bookworm**, pinned to the pulled official multi-platform digest. It binds only loopback, uses a named volume and `pg_isready` health checks. No API, worker, broker or admin UI container is started.

From repository root in Bash, prepare a private random credential once:

```bash
mkdir -p .local
if [ ! -e .local/postgres-password ]; then
  (umask 077; openssl rand -hex 32 > .local/postgres-password)
fi
export ORDERFLOW_DB_PASSWORD_FILE="$PWD/.local/postgres-password"
export ORDERFLOW_DB_PORT=5432
docker compose --profile dev config --quiet
docker compose --profile dev up -d --wait --wait-timeout 90
```

`.local/` is ignored. Do not overwrite an existing credential file, print its contents, use shell tracing with secrets, or commit real connection strings. The Compose secret file is mounted at `/run/secrets/postgres_password`; the password is not embedded in YAML or sample configuration. The daemon must see that host file: remote/isolated Docker daemons need their own secret-injection setup. `pg_isready` means the server accepts connections, not that application migrations or runtime credentials are correct.

Export the application connection privately (the generated hex password avoids connection-string delimiter escaping):

```bash
export ConnectionStrings__OrderFlow="Host=127.0.0.1;Port=${ORDERFLOW_DB_PORT};Database=orderflow;Username=orderflow_dev;Password=$(cat "$ORDERFLOW_DB_PASSWORD_FILE")"
```

Configuration is read from standard .NET providers. Hosts resolve a scoped context lazily; missing configuration gives an explicit error when persistence is requested. Normal API liveness and idle worker startup need no DB connection and do not claim readiness. Do not put the connection string in launch arguments, tracked appsettings, output logs, or .env.example. The development database owner is for local work/tests only, not a production runtime credential. Least-privilege runtime/migration-role deployment is still a later operations obligation.

## Dedicated migration step

Install SDK 10.0.114, then run:

```bash
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
```

The local manifest pins dotnet-ef **10.0.14**. EF Core/Identity use **10.0.14** and Npgsql EF/provider **10.0.3**, with central versions/generated lockfiles. The design-time factory reads `ConnectionStrings__OrderFlow`; it never hardcodes a password or starts the web host to migrate. Migrations: **20261102023642_InitialPersistence** and additive **20261103093358_AddCatalog**. The first migration includes an explicitly reviewed PostgreSQL snapshot-protection trigger. History is `messaging.__EFMigrationsHistory`.

Run migration once as an operator/deployment step before normal replicas, with a schema-owner connection. API/worker startup never calls Migrate, EnsureCreated, EnsureDeleted or seeding. EF's migration lock is additional protection, not a reason to let every replica race to migrate. On a completely empty database EF can log an initial missing-history-table probe before creating/applying the first migration; verify the final command result and history row.

For controlled deployment review, generate SQL outside the repository/build deliverables:

```bash
dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-initial.sql
```

Review it and execute through the deployment's migration procedure; do not auto-run it on API startup. Future changes require a new migration rather than editing one already deployed. The initial migration was generated from the current model; its trigger SQL is PostgreSQL-specific. If using `--artifacts-path` for builds, export matching `ArtifactsPath` when invoking EF so metadata/output paths refer to the same build.

## Explicit development seed

After applying migrations:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile -- seed-development
```

This command creates only Admin/Customer **role definitions**, using stable IDs and PostgreSQL ON CONFLICT on normalized role name in one transaction. Repeated calls do not overwrite an existing role or assign it to users. It creates **no account, password, admin membership, catalog item or stock**. The command rejects non-Development environment and pending migrations. Normal worker invocation never seeds. Milestone 04 adds a separate explicitly enabled Development-only `seed-development-account` command using private configuration; see [authentication](authentication.md). This roles-only command still creates no account. Production provisioning remains an operator responsibility, with no default credential.

## Schemas and invariants

| Schema/table | Contract and important database enforcement |
| --- | --- |
| `identity.Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims` | Guid Identity keys, explicit table/schema mapping, standard Identity indexes/FKs; Users.CreatedAt is required/finite; Identity child records retain framework cascade semantics |
| `messaging.IdempotencyRecords` | Unique `(UserId,Operation,Key)`; restrictive FK to Users; C collation for case-sensitive operation/key; visible ASCII key 1–128; operation <=100; canonical request text plus 32-byte SHA-256; response bytes/status/content type/Location/completion timestamp |
| `messaging.OutboxMessages` | EventId PK; event type/schema version/producer/aggregate identity/version/correlation/causation/time/payload; unique producer+aggregate+version+event-type effect; publish state, attempts, next-attempt, lease owner/token/expiry, published timestamp and safe last error code |
| `messaging.ProcessedMessages` | Inbox for local or external events; `(ConsumerName,MessageId)` composite PK with C collation; type/schema version/payload/hash/received time; processing state, attempts, next-attempt, lease owner/token/expiry, processed timestamp and safe last error code |

Every mapped business timestamp uses UTC DateTimeOffset -> PostgreSQL `timestamptz`, with UTC/microsecond normalization on both write/read. Required reliability timestamps reject infinities; nullable lease/terminal timestamps must agree with row state. Time is supplied by the caller's injected TimeProvider, not a DB CURRENT_TIMESTAMP default. Identity creation must set CreatedAt explicitly. LockoutEnd retains Identity's nullable storage behavior.

Shared decimal conventions use precision/scale **numeric(18,2)**; typed UsdMoney has an EF value converter. There is deliberately no fake money column or future product/order table just to demonstrate this. A test-only temporary money table exercises the inherited conventions and exact maximum-value roundtrip on actual PostgreSQL. PostgreSQL can round raw over-scale numeric input; application input/UsdMoney guards must reject it before persistence. Positive price/refund constraints arrive with their real entities.

### Idempotency storage, not an implemented checkout handler

`IdempotencyRecord.Create` accepts an already canonicalized request and computes SHA-256 over its UTF-8 bytes. Normalization/versioning is operation-specific and remains milestone 10/12 work. Store canonical text as well as hash; future replay must compare both. Request identity and canonical request/hash/creation time are immutable after insertion; PostgreSQL trigger TR_Idempotency_Immutable rejects changing them. Once Completed, its successful response snapshot is immutable as well (SQLSTATE 23514, CK_Idempotency_Immutable), including attempts to bypass the entity method via SQL/EF updates.

InProgress rows contain no response fields. Completed rows require a 2xx status, content type, exact bytea body and completion >= creation; Location is optional. Snapshot bytes are never reserialized as jsonb. `Complete` makes a defensive copy and allows one success; database CHECKs also protect shape. No cookie/trace/secret response headers are retained. No retention cleanup or key-reuse window is added.

No idempotency lease is necessary: a future command claims the unique identity and completes the snapshot **inside the same business transaction**, committing no separately unfinished claim. The table permits InProgress during that transaction; enforcing the whole command protocol remains a use-case obligation, not a claim that a row CHECK can force transaction completion. Future checkout writes its order/stock/history/outbox/snapshot with this context. No repository/independently committing store is inserted between them.

CreateOrder uses operation `CreateOrder:v1`. The existing order-scoped payment-request namespace can be represented by operation `StartPayment:v1/{orderId:D}` (within 100 characters), keeping the same user+operation+key index; payment behavior is not implemented yet. This generalizes the planned CreateOrderRequests/PaymentRequests stores rather than creating duplicate response caches. Later order/payment associations can add real FKs when those entities exist.

### Outbox/inbox storage, not publishing

Outbox states: Pending/InFlight/Published/Failed. Inbox states: Pending/InFlight/Processed/Failed. CHECKs require valid states, positive schema/aggregate versions, object-shaped jsonb, nonnegative attempts, finite/due timestamps, a full lease tuple only while InFlight, and the correct terminal timestamp only while Published/Processed. Expired leases remain valid rows; no CHECK compares to the moving wall clock. Attempt count is not a promise of exactly-once execution.

Payload hashes are 32 bytes; callers define/hash the canonical or original authenticated message representation and compare it on duplicate identity. JSONB may normalize whitespace/property order, so it is not a byte-for-byte original-request store. Do not store signing secrets or sensitive payment details in payloads/error text. LastErrorCode stores classified codes, not raw exception dumps.

Inbox has **no FK to outbox**: an incoming external message need not be a locally produced event. A message ID can be handled by different consumer names, each once durably; the PK arbitrates duplicate inserts. Message state/effects can share one transaction with business writes. Due-work and partial expired-lease indexes support future bounded queue claims. No claim/renew/ack loop, broker publishing, retries, callbacks or delivery-success simulation exists here. Future handlers must use row guards/lease-token comparisons and consumer deduplication in the same transaction, and acknowledge producer delivery only after required consumers accept it durably. Schema fields alone do not implement that protocol.

## Inspect and test real PostgreSQL

Use the container's psql without exposing the private connection string:

```bash
docker compose --profile dev exec postgres psql -U orderflow_dev -d orderflow -c '\dt identity.*' -c '\dt messaging.*'
docker compose --profile dev exec postgres psql -U orderflow_dev -d orderflow -c '\d messaging."IdempotencyRecords"' -c '\d messaging."OutboxMessages"' -c '\d messaging."ProcessedMessages"'
```

For the full solution suite, the test administrator needs CREATE DATABASE permission:

```bash
export ORDERFLOW_TEST_POSTGRES="$ConnectionStrings__OrderFlow"
dotnet test OrderFlow.slnx -c Release --no-build --no-restore
```

The PostgreSQL fixture creates a unique `orderflow_test_<guid>` database, migrates it, tests real constraints/transactions/stores/converters, and drops **only that test-created database** afterward. It never drops or clears the configured development database or volume. Missing test configuration fails clearly, with no EF InMemory/SQLite fallback and no misleading skip. Provider tests run serially in their fixture; this milestone makes no race-test claim. Future concurrency tests still need deterministic gates at the contested SQL operation.

For the external-service-free HTTP/primitive/composition subset only:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

Current suite: 43 unit + 49 prior HTTP/composition + 10 PostgreSQL tests = **102 tests**. The new unit coverage checks snapshot completion guards/buffer isolation. SQL coverage includes clean migration/model drift, named indexes/constraints, user-scoped unique keys, snapshot bytes/hash/immutability, FK insertion/deletion protection, independent event/consumer identities, invalid lease/state/attempt metadata, shared commit/rollback, UTC/precision roundtrip, repeatable roles-only seed, and scoped context/Identity store resolution from both hosts. No auth/ownership/order/broker correctness is claimed.

## Persistent volume and manual reset

PostgreSQL 18 stores versioned PGDATA below `/var/lib/postgresql`; the named volume mounts that parent, not the old `/var/lib/postgresql/data` path. Its Compose name is normally `<project>_postgres_data`. `docker compose --profile dev down` removes the container/network but **keeps the volume**. Starting the same project again retains migrations/users/data; this was verified through a down/up cycle with seeded roles. Password file/POSTGRES_DB/POSTGRES_USER changes initialize only a new empty volume; editing them does not rotate an existing database's credentials or rename it.

For a fresh disposable workspace while preserving old data, use a new project name and unused loopback port, keeping or generating a matching private password file:

```bash
export ORDERFLOW_DB_PORT=55432
docker compose -p orderflow-fresh --profile dev up -d --wait
```

Update the private connection/test settings to that port, then run the dedicated migration/seed steps. The original project volume remains untouched.

If you intentionally want to erase disposable dev data, first identify the exact project/volume and back it up if needed. Only then, manually run `docker compose -p <the-disposable-project> --profile dev down --volumes`, followed by normal up/migrate/optional seed. **That command permanently deletes that project's volume; it is never executed by the application, tests against the configured dev database, startup, seed command, or a reset script.** No automatic reset command was added or run against user data. The verification volume is retained after stopping its project.

## Catalog migration (07)

After building, run the same dedicated `dotnet ef database update` step above; it now also applies 20261103093358_AddCatalog. Existing Identity/reliability data is retained, and repeated update is a no-op. Catalog creates Categories/Products, required restrictive Category FK, unique normalized SKU, numeric(18,2) positive-price/finite-time/name/version checks and the documented B-tree ordering/filter indexes. No Inventory column/table or default catalog seed is added. See [Catalog](catalog.md) for actual query/index behavior and HTTP verification. Direct raw SQL numeric input can be coerced by numeric(18,2); API/domain reject extra scale before writes. Down removes Catalog data, so never run a rollback/reset against data you need.
