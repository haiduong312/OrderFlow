# ADR-009: One scoped PostgreSQL context and durable reliability schema

Date: 2026-11-02. Status: Accepted. Extends ADR-001/004/005; generalizes the planned command-response tables without changing replay or delivery guarantees.

## Context

Milestone 03 requests PostgreSQL, Identity storage and shared idempotency/outbox/inbox foundations before business entities. The user explicitly defers auth endpoints to **04**, whereas the previous roadmap put React at 04 and Identity endpoints at 05. Reported this mismatch before implementation; swap those pending deliverables/dependencies (04 auth, 05 React). No auth endpoint is implemented now. Reliability persistence is also pulled forward from the earlier 10/11/14 storage prerequisites; those milestones retain their use-case/delivery work.

## Decision

Keep the three production projects and one scoped IdentityDbContext-compatible OrderFlowDbContext. Explicitly map Identity Guid storage to identity schema and IdempotencyRecords/OutboxMessages/ProcessedMessages to messaging. Use one migration chain/history table and actual PostgreSQL 18.6. API/worker register the same scoped context/Identity stores but resolve connection options lazily; no startup connection, migration, seed or fake DB readiness. EF/Identity and dotnet-ef pin 10.0.14; Npgsql EF/provider pin 10.0.3. Do not enable generic connection retries or a repository abstraction.

IdempotencyRecords generalizes the previously planned CreateOrderRequests/PaymentRequests tables. Uniqueness is UserId+Operation+Key with ordinal C collation, a restrictive Identity-user FK, canonical text/SHA-256, and exact successful response bytes/type/status/Location. Operation naming retains version and optional order scope. CHECKs enforce shape; a small PostgreSQL trigger freezes request identity and completed response data. InProgress exists only for a future transaction's claim phase; future command handlers must not commit incomplete claims separately. No key-expiry cleanup is introduced.

Outbox persists a versioned envelope's metadata, object payload and durable publish state. Inbox persists consumer+message identity/type/version/payload hash and processing state. Both carry attempts/due time/lease owner/token/expiry/terminal timestamp with state-consistent constraints and queue indexes. Inbox deliberately has no local-outbox FK so external events remain independent. A shared context lets business effects, idempotency completion, outbox insert and inbox completion commit/roll back together; storage does not claim to implement the later dispatch/lease protocol or exactly-once side effects.

Apply UTC/microsecond DateTimeOffset conversion and decimal/UsdMoney numeric(18,2) conventions without inventing future monetary entities. Use finite timestamp/version/count/hash constraints and bytea for original responses; jsonb is appropriate for event payloads but not byte replay. Explicit future entity mappings/positive-price rules remain required.

Use a PostgreSQL-only Compose dev profile, pinned official image/digest, loopback port, health check and named volume at PostgreSQL 18's parent data directory. Private file-backed secret, no shipped password. Migration execution is a separate EF CLI/operator step. Development-only role seeding is an explicit Worker command, transactional INSERT ON CONFLICT, no users/passwords/admin assignment. Existing volumes are never reset automatically.

## Consequences and verification

Shared context/schema releases couple the modules intentionally. Database constraints and an immutable-snapshot trigger protect durable facts across processes; future business rules still need transactional guards. PostgreSQL-specific SQL/provider semantics are integration-tested on real PostgreSQL, not EF InMemory. Development owner privileges are not the future production runtime-role policy. Test fixtures own and clean only generated test databases; developer data/volume persist until explicit operator reset.

Acceptance evidence includes applying the initial migration to a clean database, repeat/no-drift checks, schema/index/constraint inspection, scoped API/worker/Identity stores, original snapshot bytes and immutable updates, unique/FK/state failures, shared commit/rollback, real numeric/UTC conversion, explicit seed twice, and down/up volume retention. Details, Docker environment workaround and unverified limits are recorded in [status](../implementation-status.md); commands/data lifecycle are in [persistence guide](../persistence.md).

Official references: [Identity EF model](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0), [EF design-time factory](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation), [Npgsql 10 provider](https://www.npgsql.org/efcore/release-notes/10.0.html), [Npgsql timestamps](https://www.npgsql.org/doc/types/datetime.html), [official Postgres image](https://hub.docker.com/_/postgres), [PostgreSQL 18.6](https://www.postgresql.org/docs/release/18.6/), and [trigger syntax](https://www.postgresql.org/docs/18/sql-createtrigger.html).
