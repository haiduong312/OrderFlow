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

When `ORDERFLOW_TEST_POSTGRES` is absent, every test tagged `Category=PostgreSQL` requires Docker (Ordering checkout/lifecycle/expiry worker, Catalog, Inventory, Identity, representative-resource HTTP policies, persistence and harness checks). Docker must permit container creation, image pulls, random published ports and Ryuk. Missing Docker/access/image readiness causes an explicit failing prerequisite with the original diagnostic; it is never silently skipped or replaced with EF InMemory/SQLite. Startup has a 90-second cancellation budget after container construction; Docker endpoint discovery uses Testcontainers' own transport limits.

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

Pass `clock: new ManualTimeProvider(startUtc)` to the factory and call Advance for business deadlines. This clock overrides GetUtcNow with thread-safe monotonic manual advances; it does **not** simulate timers or Task.Delay. Expiry tests explicitly invoke OrderExpiryIteration.ProcessAsync after advancing time; scheduler shutdown tests separately control/observe the hosted loop. The harness regression verifies Identity/Product timestamps, an Inventory movement at logical +15 minutes and cookie rejection after its lifetime, without real sleeps. Framework Identity lockout still uses its built-in UTC clock per ADR-010; this change does not claim to replace that framework clock.

API registers no business loops; the separate Worker registers expiry only. The factory permits only the framework web-host startup and ephemeral Data Protection warmup hosted services; an unknown IHostedService (including factory/instance registrations) fails host construction before it can run. The regression injects an uncontrolled loop and checks rejection. When adding workers, remove their registrations explicitly in the test factory and drive a scoped single iteration manually; do not broadly remove all IHostedService registrations, including the web server. The service-free Worker smoke test explicitly sets OrderExpiry:Enabled=false; seed mode also suppresses jobs. PostgreSQL expiry tests drive iterations with jobs suppressed, except tests explicitly starting/stopping the scheduler.

Representative-resource controllers remain test-assembly probes with real cookie/role/resource policies; their in-memory resource dictionary represents an interface contract, not an Order persistence test. Production-host regressions verify probe paths are absent. Current Catalog/Inventory/checkout endpoints are tested directly, with actual PostgreSQL and production CSRF/authorization behavior. Checkout now includes owner-constrained Order detail SQL/HTTP tests; order lists/cancellation/Admin workflows are covered by milestone 13, and worker expiry by the 14 expiry slice. See [checkout](checkout.md).

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

Creating and migrating a database per case costs more than shared-data resets, but handles append-only triggers and HTTP transactions without fixture coupling. Docker startup/pulls need local capacity; CI can cache pinned images. Serial collection execution favors repeatability over throughput. A pre-lock rendezvous proves both contenders arrived at SQL; it does not assert which one wins, measure lock wait duration or prove separate OS-process crash recovery. Framework hosted-service type names and SQL match predicates are deliberate seams: review them on framework/SQL upgrades. Logical time controls UTC decisions; expiry tests drive callable iterations explicitly. Payment, OS-kill/network-loss worker faults and least-privilege runtime-role deployment remain future work.

## Checkout races (12)

CreateOrder same-key tests rendezvous before the unique claim INSERT, with no business locks held. Last-unit checkout tests rendezvous immediately before each inventory FOR UPDATE; earlier claims are distinct and category/product FOR SHARE locks are mutually compatible, so both requests can reach the contested statement without a false barrier deadlock. Both participants are counted by distinct DbContext ID, then final orders/items/reservations/ledger/outbox/claims are reconciled through a fresh context. Do not replace those SHARE locks with exclusive catalog locks while retaining the same downstream barrier. Single-request one-way pauses test catalog reassignment and logical decision time. See [checkout walkthrough](checkout.md) for all transaction stages and fault evidence.

## Order management races (13)

Same-order cancel/Admin competitors rendezvous BEFORE Order FOR UPDATE, counting distinct contexts. Never put a two-party inventory barrier after one request has locked the shared order; the loser cannot reach it. Only the winning cancellation reaches inventory; the other reads the committed Cancelled/Expired state without a new effect. Separate tests cancel distinct orders sharing products with canonical balance order. A one-way pre-order-statement arrival signal lets a test release a separately held order lock after advancing logical time, without waiting for the blocked request to acquire it. Read-only views cause no expiry writes. Upgrade/fault tests verify renamed history preservation, terminal guards and rollback of already executed releases when outbox fails. See [order management](orders.md).

## Expiry worker races and scheduling (14 expiry slice)

See [expiry runbook](expiry-worker.md) for the full test matrix and commands. Two workers rendezvous after claiming different SKIP LOCKED order rows, before any inventory lock; both can reach that boundary without waiting for a shared held lock. Same-order cancellation/expiry rendezvous before Order locking SQL. One-way pauses verify pre-deadline cancellation ownership and scheduled shutdown after executed release SQL. Worker passes get a new async DI scope/context per order and never run implicitly in HTTP tests. The 300-second polling delay is observed then cancelled, not slept through. Every database case uses the existing serial PostgreSQL collection/per-case schema/cleanup budgets.

## Payment and simulator boundaries (15 / submission slice of 16)

[Payment tests](payments.md) use the existing PostgreSQL collection/per-case migrations and an additional owned provider database where needed. SimulatorTestHost runs real Kestrel HTTP on a random loopback port, with generated distinct secrets and a replaceable UTC clock. Start/stop each have 15-second cancellation budgets and final disposal is awaited at most 15 seconds; clients/hosts close before owned database teardown. No external simulator service or real provider is required. Missing Docker/PostgreSQL still fails honestly; unit/service-free checks never start the simulator or a database.

Payment loops are opt-in and disabled in normal HTTP fixtures; tests call PaymentProcessingIteration explicitly. Composition tests inspect enabled Worker registrations and seed-mode suppression without starting network loops. Initiation gates wait BEFORE Order FOR UPDATE. Provider idempotency gates wait BEFORE the first advisory lock, counting distinct contexts once. Lease-fencing tests pause after provider response with merchant transactions disposed, then reclaim using logical +60 seconds; no second participant is blocked behind a held business lock. One-second client timeouts are deliberate transport failure scenarios, not arbitrary sleeps claiming race correctness.

Tests prove original client response bytes, one active attempt/order, unchanged stock/history/outbox during payment observation, independent provider persistence across HTTP-host restart, and no transaction held over gateway I/O via an independent NOWAIT lock probe. Callback tests validate generated raw-body HMAC/redelivery identity, not API receipt/settlement. Provider refunds are tested independently; no merchant refund workflow is claimed.

## Signed callbacks and settlement

PaymentCallbackTests uses actual PostgreSQL and real cookie/CSRF setup; the provider callback requires HMAC instead of cookies/CSRF. Receipt competitors rendezvous before INSERT; settlement/payment-versus-cancel/expiry competitors count independent contexts before Order FOR UPDATE. One-way pauses and held row locks distinguish order-lock decision time from later inventory wait. Fresh contexts reconcile stocks, ledger, reservation disposition, capture/refund identity and inbox. Faults prove receipt/inbox rollback and rollback of already executed consumption when mandatory outbox fails. See [callback guide](payment-callbacks.md) for focused commands and [status](implementation-status.md) for actual run evidence.

## Compensation execution and payment lookup

PaymentRecoveryTests uses independent merchant/provider databases and real simulator HTTP. Refund claim gates count distinct contexts before SELECT FOR UPDATE SKIP LOCKED; callback/lookup races meet before the shared Order lock. A provider-after-effect signal and logical lease advancement test stale completions; cancellation at that boundary models shutdown. Fault injection rejects local success acknowledgement after the provider commit; restart/lookup recovers the same identity. A separate NOWAIT connection proves no merchant refund lock is held over lookup/refund HTTP. Tests check one effective provider refund/capture, local result/outbox, unchanged terminal order/stock disposition, mismatch review, missing/pending/failed lookup and populated migration preservation. One-second transport timeouts intentionally test uncertainty; no arbitrary sleeps claim race correctness. See [recovery guide](payment-recovery.md) and [status](implementation-status.md).

## RabbitMQ publication tests

OutboxPublishingTests uses a separate nonparallel Messaging collection with actual disposable PostgreSQL and RabbitMQ 4.4.0; other PostgreSQL tests do not require a broker. Per-case database/topology names isolate evidence. Tests await real tracked confirms/mandatory returns/queue-full NACKs, not a fake successful publisher. A frame-aware TCP proxy forwards requests to the real broker and drops only confirm frames; after a real timeout/retry, two bodies carry the same EventId. A SQL acknowledgement fault and fake lease time model the post-confirm crash window. Independent contexts rendezvous before SKIP LOCKED; a paused old owner proves fenced completion. A NOWAIT connection probes the claimed row during actual publication, proving no held transaction. Uncommitted outbox rows are invisible. Restart proves persistent queued delivery. Consumer callbacks/TCS observe finite dead lettering without race sleeps. Full process kill, clustered broker partitions and production durability configuration remain unverified. See [runbook](messaging.md) and [status](implementation-status.md).

Focused command:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OutboxPublishingTests|FullyQualifiedName~OutboxCompositionTests|FullyQualifiedName~EventEnvelopeTests'
```

The external-service-free filter is now `Category!=PostgreSQL&Category!=RabbitMQ`. RabbitMQ collection prerequisites fail visibly; no broker-less success skip.

## Notification consumer and API tests

NotificationTests uses actual PostgreSQL/RabbitMQ in the Messaging collection, including two real subscriptions with prefetch one. Both distinct contexts rendezvous immediately before the unique inbox INSERT, and a safe post-ACK log signal observes completion. Commit-before-ACK fault injects a failure at the actual ACK boundary, checks committed inbox/notification/outbox through a fresh context, closes the real channel and processes its redelivery. Broker callback/TCS observers see malformed/unsupported/identity/recipient/oversized poison and transient SQL rollback exhaust the finite topology into the dedicated DLQ, without arbitrary sleeps. SQL tests check unique effect, owner correlation, immutable/read/handled marker guards; populated prior-schema migration preserves original events/orders.

NotificationHttpTests uses real cookies/CSRF/Identity and PostgreSQL for list/detail/count/read scoping, Admin/non-owner denial, invalid inputs, duplicate-after-read preservation and two independent read requests rendezvousing before their conditional UPDATE. Service-free parser tests cover seven supported types and fourteen invalid/unsupported cases; composition tests keep opt-in/seed/load bounds explicit. Focused command and exact executed evidence are in [notification runbook](notifications.md) and [status](implementation-status.md). No OS-kill, clustered failover, email/realtime or exactly-once transport claim.

## Email delivery tests and demo

The dedicated nonparallel Email collection owns actual PostgreSQL/Mailpit; other PostgreSQL tests do not require SMTP. A controllable gateway store persists across merchant service-provider restarts within a test, models lost acceptance results and advertises capabilities explicitly. NOWAIT probes at lookup/send check no held merchant transaction. Claims/inbox/Admin races use independent contexts and SQL gates; provider acceptance/shutdown uses TCS synchronization. Real SMTP plus rejected local Sent acknowledgement proves duplicate mail after recovery; Mailpit recipient-filtered messages_count is used (total counts the entire sink). Docker random port is refreshed after fixture restart. The executable [outage demo](email-delivery.md) stops actual Mailpit before checkout/payment, confirms stock/capture while mail stays Pending, then recovers the same email. Full provider/OS/cluster guarantees are not inferred from this fake or sink.

The service-free filter is now `Category!=PostgreSQL&Category!=RabbitMQ&Category!=SMTP`. Missing SMTP prerequisites fail visibly. Focused command: `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Email'`.

## SignalR/Redis realtime tests

Realtime collection owns actual PostgreSQL/RabbitMQ/Redis and starts two Kestrel API hosts with separate DI/SignalR managers, sharing only fixture cookie-key material and database. Official native SignalR clients use real cookies, CSRF negotiation, Origin and WebSockets. ConnectionReady/client callbacks/log signals observe subscription readiness, duplicates and Redis failure without race sleeps; actual auth-expiry timer and API shutdown close sockets. A consumes committed outbox while B owns connections; tests verify user isolation/minimal IDs/HTTP catch-up and finite invalid/failure DLQ. Service-free tests cover strict/canonical user identifiers and opt-in configuration. See [runbook/demo](realtime.md) and [status](implementation-status.md). The external-service-free filter now also excludes Category=Redis: `Category!=PostgreSQL&Category!=RabbitMQ&Category!=SMTP&Category!=Redis`. No new DB migration or frontend.


## Direct reporting and read-only audit tests

[Reporting runbook](reporting.md) / [ADR-026](adr/0026-direct-admin-reporting.md) document all report/audit tests. The existing PostgreSQL collection owns per-case migrated databases; this module needs no broker/Redis/SMTP. ReportingScenario uses actual checkout/fulfillment/settlement/refund domain paths, fixed logical UTC dates and exact amounts; auth is refreshed after multi-day clock advancement. Tests cover microsecond/UTC/exclusive boundaries, DB session timezone independence, paid versus completed/created, immutable item prices, multi-item join multiplication, pending/review/completed refunds, negative net, active low-stock calculation, read-only paginated DTOs and Admin/Customer denial.

A one-way TCS gate after the first order-existence SELECT pauses history counting, lets a separate Admin transaction append history, then releases the report; count/page retain one Repeatable Read snapshot and the next request sees the new fact. No sleep establishes the race. The index-only migration upgrade test preserves populated identities/totals and verifies all five indexes. Reporting has no durable event projection; source-settlement replay is tested without inventing a reporting inbox/checkpoint.

Focused command: `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Reporting'`. Query-plan tests save full report SQL EXPLAIN JSON and isolated 100,000-row date/order/partial-index experiments to `/tmp/orderflow-reporting/plans`. Synthetic timing is not a production performance percentage; inspect paths/cardinalities and repeat with representative production data before extending indexes.


## React browser acceptance

[Frontend runbook](frontend.md) / [ADR-027](adr/0027-react-session-foundations.md) define the Node/Vite/Playwright prerequisite and real browser suite. Run npm ci and npx playwright install chromium in client before a full solution test; the default suite now includes thirty-one Category=Browser/PostgreSQL cases (sixteen original session/shopping + nine checkout/live + six Admin; the latter fifteen also use Redis). For backend-only use Category!=Browser. Service-free filters already exclude PostgreSQL and therefore browser cases. No missing-prerequisite success skips or fake API data.

Each browser theory row owns migrated PostgreSQL + real Kestrel with ephemeral cookie keys, generated users/stock, an injected UTC clock, random loopback API/Vite ports and a bounded Node/Playwright process. A random-header clock endpoint exists only in that test-host method; a separate normal-API test verifies its absence. Logical +9h proves ticket expiry without sleep. API quota/auth/password/CSRF behavior is unmodified, isolated per host. Same-SPA account switching preserves the original QueryClient so the test cannot mask leakage by reloading; two-tab cookie changes, private data, error/trace/field handling and viewport/keyboard behavior are exercised. Network gates/aborts/header tampering drive faults without manufacturing successful responses. Traces/video are off; per-case processes/hosts/DBs are cleaned up before fixture disposal. Vitest additionally checks cache/token/error generations and exact cents without external services.


Milestone 20 adds seven shopping cases using real catalog fixtures and Admin cookie/CSRF mutations: complete keyword/category/sort/page key changes, stable pagination, category paging/detail/404/out-of-stock, exact totals/quantity validation/reload/Remove/Clear, price-change revalidation, archived/short-stock/transport error, intentional shared-device account scope and storage-event cross-tab sync. New Vitest suites exercise real QueryClient cancellation/parameter forwarding plus immutable/bounded/migrated/sanitized cart state and money overflow/missing prices. No EF InMemory or fabricated successful browser responses. See ADR-028/runbook/status for staleness and test artifacts.


## Checkout/payment/live browser acceptance

ADR-029 adds nine actual CheckoutBrowserTests using merchant PostgreSQL + independent simulator PostgreSQL/HTTP, normal Identity/CSRF, production submit/reconcile/refund/notification services, Redis and official browser SignalR. Cases cover real 201 loss/replay with exact saved header/body and changed cart, A/B recovery isolation/owner 404, actual client 15s payment initiation timeout after committed 202, server expiry, late capture/confirmed refund and NeedsReview, eligible cancellation, mark-read/source dedup, actual network socket abort/renegotiation gate/durable catch-up and duplicate hints. Existing sixteen session/shopping flows remain. Owner payment-status tests verify 401/403/404/no-store/bounds/paging and absence of provider/raw/lease fields. Normal API test controls stay absent. Browser traces/video remain disabled; fixture-only passwords/nonces pass through environment. See [frontend runbook](frontend.md) and exact [status](implementation-status.md).


## Admin workspace acceptance

[ADR-030](adr/0030-admin-workspace.md) / [Admin runbook](admin-ui.md) add six real browser cases to the existing CheckoutBrowserTests harness. They cover catalog/category archive/restore and versions, signed stock reasons and the Reserved floor, loss of an actual committed 201 and exact operation replay through logout/customer/Admin restoration, verified simulator capture followed by all allowed fulfillment edges, immutable history/UTC reports, stale concurrent catalog edits and direct Customer API denial despite forged local roles, committed email-retry response loss with one audit/original delivery/provider identity/unchanged lifetime counter, pagination/empty/error/retry/responsive tables.

A test-only nonce-protected driver prepares a coherent NeedsReview email from a real confirmed order/source event. Normal API hosts do not map the driver. Browser application code never calls test controls. Response/route gates wait for actual completion; no fake successful data, disabled auth/CSRF, arbitrary sleeps or EF InMemory are used. Traces/video remain off; screenshot fixture users are generated example.invalid addresses. Existing backend suites independently test SQL stock/state/history immutability and email lock/replay races. Vitest tests scoped keys, AbortSignal, targeted invalidation, bounded exact recovery/body/identity, failures, safe closure, synchronous duplicate submit, exact signed money and UTC/int boundaries.

Focus with `--filter 'FullyQualifiedName~CheckoutBrowserTests&DisplayName~admin_'`; run the complete frontend integration group with `--filter 'FullyQualifiedName~Frontend'`, or the full solution as before. New artifacts are under `/tmp/orderflow-admin-ui/{artifacts,results,browser-artifacts}`. Exact commands/results, including initial failures and their corrections, are in [status](implementation-status.md).
