# Integration-test harness — milestone 08a

The harness extends the existing tests; production endpoints, authentication, SQL coordination and migrations are unchanged. See [ADR-014](adr/0014-integration-test-harness.md) and [implementation status](implementation-status.md) for decisions and execution evidence.

## Run tests

Prerequisites: SDK **10.0.114**, NuGet access on first restore, and a reachable Docker daemon for PostgreSQL tests. The test-only dependency **Testcontainers.PostgreSql 4.16.0** is centrally pinned and locked. Testcontainers starts the same PostgreSQL **18.6-bookworm** image/digest as Compose on a random port, with a generated disposable password. Compose and application connection configuration are unnecessary for the default suite. Keep the Testcontainers resource reaper (Ryuk) enabled; never enable reusable containers for these tests.

From the repository root:

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet test OrderFlow.slnx -c Release --no-build --no-restore
```

When `ORDERFLOW_TEST_POSTGRES` is absent, every test tagged `Category=PostgreSQL` requires Docker (Catalog, Inventory, Identity, representative-resource HTTP policies, persistence and harness checks). Docker must permit container creation, image pulls, random published ports and Ryuk. Missing Docker/access/image readiness causes an explicit failing prerequisite with the original diagnostic; it is never silently skipped or replaced with EF InMemory/SQLite. Startup has a 90-second cancellation budget after container construction; Docker endpoint discovery uses Testcontainers' own transport limits.

For machines without Docker, optionally set `ORDERFLOW_TEST_POSTGRES` **privately** to an actual PostgreSQL 18 administrator connection with CREATEDB. This overrides Testcontainers, does not start/stop that server, and still creates separate test databases. The configured administrator database is never cleared or dropped. Invalid credentials, unreachable servers and insufficient privileges remain real failures. Do not print the connection, put it in command arguments, or use a production administrator account.

The independent unit suite requires no external services:

```bash
dotnet test tests/OrderFlow.UnitTests -c Release --no-build --no-restore
# All service-free unit/HTTP/policy/composition/harness-synchronization tests:
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
# Actual PostgreSQL subset, or just inventory races:
dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore --filter 'Category=PostgreSQL'
dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~InventoryConcurrencyTests'
```

On a host without accessible Docker and without the optional PostgreSQL override, the PostgreSQL subset must exit nonzero with `PostgreSQL integration prerequisite unavailable` and the underlying Docker diagnostic. Do not stop a shared daemon just to test this. A bad DOCKER_HOST alone is not a reliable simulation: Testcontainers can discover another reachable local endpoint. See status for the direct initializer check under restricted socket permissions.

## Lifetime, isolation and parallel rules

`PostgresCollection` owns one `PostgresServer` for the PostgreSQL collection. xUnit creates a fresh class instance for each fact/theory row. Every PostgreSQL class derives from `PostgresTest`: InitializeAsync creates `orderflow_test_<guid>` and applies **all real migrations** via IMigrator before the body runs; DisposeAsync drops only that generated database. There is no shared mutable test database, truncation, trigger disabling, ambient rollback wrapper, EnsureCreated or fixture dependency on test order. The migration-upgrade test requests its own additional database at AddCatalog, then upgrades it through AddInventory.

PostgreSQL collection parallelism is explicitly disabled. Individual HTTP race tests use independent requests/scopes/contexts, including two distinct WebApplicationFactory hosts; no DbContext is shared across tasks. Separate test runner processes can run independently because all database/container identities and ports are unique. Service-free collections may use normal xUnit parallelism. Do not move DB tests out of this collection or add process-global environment mutations inside tests.

Test connection pooling is disabled, avoiding one retained pool per disposable database. Infrastructure uses wall-clock budgets independently of injected business time: connect 5s, PostgreSQL lock timeout 10s, server statement timeout 20s, Npgsql cancellation timeout 1s; the production EF command timeout remains 30s. Database creation/migration gets 60s cancellation; forced database drop gets 30s with its own fresh token. Container DisposeAsync is awaited for at most 30s; its API has no cancellation token, so Ryuk remains the recovery path if cleanup times out or the runner dies. Cleanup failures are reported, never swallowed. Initialization failures attempt cleanup immediately because xUnit does not guarantee teardown after failed initialization. Use cancellation/budgets; never advance fake time to drive infrastructure cleanup.

Dispose clients, factories, contexts and transactions before test teardown with using/await using. A forced drop can terminate leaked connections only in that owned test database. The cleanup regression deliberately leaves one such connection open and verifies the owned database disappears while another test database survives. It also checks repeated disposal. The two isolation theory rows use the same SKU and assert an empty migrated schema independently.

## HTTP, Identity, CSRF, time and background work

The shared `AuthApiFactory` is the existing WebApplicationFactory used across Identity, Catalog, Inventory and resource-policy tests. It keeps Production as the default, points persistence at the test-owned database, uses ephemeral Data Protection keys and accepts an optional TimeProvider plus test-only SQL interceptors. All replacements are in the test assembly through ConfigureTestServices; no production configuration flag, fake auth scheme or test endpoint is added.

`AuthenticatedBrowser.CreateAsync(factory, "Admin")` (or Customer/both) persists a generated user and role membership through the real Identity stores, then logs in through `/api/auth/login`. Each Browser() has its own cookie jar, HTTPS base address and redirects disabled. `BrowserRequests.TokenAsync` calls the real antiforgery endpoint. `SendAsync` accepts the explicit token and cancellation token; pass null for negative CSRF tests. Refresh the token after login/logout; acquire each race participant's token before releasing competing operations. Helpers do not disable rate limits, password hashing, authentication or antiforgery filters. Use a fresh factory per test to isolate quotas and ephemeral key material. Separate hosts require separate logins because their test key rings are intentionally distinct.

Pass `clock: new ManualTimeProvider(startUtc)` to the factory and call Advance for business deadlines. This clock overrides GetUtcNow with thread-safe monotonic manual advances; it does **not** simulate timers or Task.Delay. Future expiry tests should explicitly invoke one business operation/worker iteration after advancing time. The harness regression verifies Identity/Product timestamps, an Inventory movement at logical +15 minutes and cookie rejection after its lifetime, without real sleeps. Framework Identity lockout still uses its built-in UTC clock per ADR-010; this change does not claim to replace that framework clock.

No business loops exist today. The factory permits only the framework web-host startup and ephemeral Data Protection warmup hosted services; an unknown IHostedService (including factory/instance registrations) fails host construction before it can run. The regression injects an uncontrolled loop and checks rejection. When adding workers, remove their registrations explicitly in the test factory and drive a scoped single iteration manually; do not broadly remove all IHostedService registrations, including the web server. Worker composition smoke tests currently start an idle host with no business jobs.

Representative-resource controllers remain test-assembly probes with real cookie/role/resource policies; their in-memory resource dictionary represents an interface contract, not an Order persistence test. Production-host regressions verify probe paths are absent. Current Catalog/Inventory endpoints are tested directly, with actual PostgreSQL and production CSRF/authorization behavior. Order ownership queries remain future milestone scope.

## Contested-operation synchronization

`RaceRendezvous` counts distinct unit-of-work IDs and releases when all participants arrive; it waits at most 15s and propagates cancellation/timeout to waiting peers. `CommandRaceGate.ReaderExecutingAsync` places the hook **before SQL executes**, not after a row lock is acquired. Each context rendezvous only once, so later sorted-row locks never introduce a second barrier. Tests assert the participant count instead of assuming Task.WhenAll proves contention. No arbitrary sleeps provide concurrency evidence.

Safe existing boundaries:

- Inventory removals/receipts and login: immediately before the first contested FOR UPDATE, with neither request owning that shared row yet.
- Multi-row InventoryLocking: before the first sorted product lock only; later rows are traced but never wait again.
- Catalog duplicate SKU insert: requests use **different categories**, so earlier category locks are disjoint. Same-category requests must synchronize before their category lock instead.
- Cross-product stock-operation collision: requests hold **different balance rows** before their inserts contend on the shared unique operation identity. Never use this insert barrier for same-product requests; use the earlier balance-lock hook.
- Identity registration: after uniqueness validation and before inserting either user.

A gate is test orchestration, never the production correctness mechanism. PostgreSQL locks/unique constraints/transactions arbitrate the result. For a future race with an earlier common order/idempotency lock, move the rendezvous before that **first common lock**, or hold one participant with a one-way signal and observe the other waiting using a separate PostgreSQL observer connection; never demand both reach a downstream barrier behind that lock. Use an injected test-assembly interceptor at these boundaries; only introduce a narrow production no-op collaborator if SQL interception cannot expose the necessary safe boundary, and never add unauthenticated test routes.

The genuine inventory race uses two API hosts and two authenticated browsers to remove 7 from an opening 10. It proves two different contexts reach the first balance lock, asserts exactly one 201 and one `409 insufficient_stock`, then reads a fresh context to verify OnHand=3, Reserved=0, Revision=2, contiguous ledger revisions 0/1/2, reconciled deltas, the winning operation's before/after snapshots and no losing movement. Existing receipt, replay, cross-product collision, ordered multi-row, SKU and Identity races use the same rendezvous strategy.

## Trade-offs and limits

Creating and migrating a database per case costs more than shared-data resets, but handles append-only triggers and HTTP transactions without fixture coupling. Docker startup/pulls need local capacity; CI can cache pinned images. Serial collection execution favors repeatability over throughput. A pre-lock rendezvous proves both contenders arrived at SQL; it does not assert which one wins, measure lock wait duration or prove separate OS-process crash recovery. Framework hosted-service type names and SQL match predicates are deliberate seams: review them on framework/SQL upgrades. Logical time controls UTC decisions only; timer-based workers need explicit iteration control later. Full reservation/expiry/payment/worker fault tests and least-privilege runtime-role deployment remain future work.
