# OrderFlow

A portfolio modular monolith for ASP.NET Core / full-stack development. Current delivery adds a bounded disposable failure/load lab: pinned k6 through two actual API replicas, cache off/on measurements, stock/idempotency bursts, broker recovery and nine deterministic real-dependency fault cases. [Reproduction and measured limits](docs/load-testing.md) distinguish HTTP samples, logical-time recovery and unmeasured OS-kill behavior. See also [CI](docs/ci.md), [deployment](docs/deployment.md), [frontend](docs/frontend.md) and [implementation status](docs/implementation-status.md). No production target, public publication, volume deletion, push or remote workflow run is included.

Read the [project contract](docs/project-spec.md), [architecture](docs/architecture.md), [ADRs](docs/adr/README.md), and [implementation status](docs/implementation-status.md) before adding features. Repository working rules are in [AGENTS.md](AGENTS.md).

## Prerequisites

- Install the repository-pinned **.NET SDK 10.0.115**, including its ASP.NET Core runtime, from the [official .NET 10 download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- `global.json` selects this exact SDK, disables preview selection and automatic SDK roll-forward. `dotnet --version` from the repository should print `10.0.115`. SDK upgrades are deliberate changes followed by restore/build/test, not machine-dependent substitutions.
- NuGet.org access is required on the first restore. Docker is required for the default Testcontainers integration suite (an explicit actual PostgreSQL connection is also supported); payment tests self-host the restricted simulator on loopback, without an external payment provider. Full tests now also need Node **24.19.0**, npm **11.17.0**, client dependencies and Playwright Chromium; backend-only runs can use `--filter 'Category!=Browser'`.

Follow the [PostgreSQL guide](docs/persistence.md) to generate a private password file, start the PostgreSQL-only Compose dev profile, export connection settings and run the **dedicated migration step**. The guide documents safe repeatable seeding, persistent volumes, inspection and manual reset. API/worker never migrate or seed automatically. Tests automatically start disposable PostgreSQL through Testcontainers. An optional private ORDERFLOW_TEST_POSTGRES connection with CREATE DATABASE permission overrides Docker. See the [testing guide](docs/testing.md) for lifecycle, per-case isolation, synchronization and service-free commands.

For the separate deployment rehearsal, run `python3 infra/deployment/setup.py` and follow the [explicit build → dependencies → migrate → replicas steps](docs/deployment.md). Container builds have their own published SDK10.0.404 pin; local SDK selection remains10.0.115. `bash infra/deployment/verify.sh` creates an isolated test project and preserves its volumes. Base `dev` starts PostgreSQL only; messaging/email/cache/realtime stay optional. Nothing is published beyond loopback.

## Restore, build, and test

Run from the repository root:

```bash
dotnet --version
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx --configuration Release --no-restore
# Docker running: Testcontainers starts PostgreSQL automatically.
dotnet test OrderFlow.slnx --configuration Release --no-build --no-restore
```

The solution contains six projects (three production core, one optional Development/Testing simulator, two tests). Tests cover domain/HTTP/security and actual PostgreSQL concurrency, plus a dedicated real RabbitMQ collection for routing/confirms, uncertainty, restart, lease fencing and finite dead lettering. Tests own fresh databases/containers and never migrate/reset the developer database. Missing prerequisites fail visibly. For the external-service-free subset use `--filter 'Category!=PostgreSQL&Category!=RabbitMQ'`. See [testing](docs/testing.md) for setup and [implementation status](docs/implementation-status.md) for exact current counts/results.

The same CI gates run locally through `bash infra/ci/build-backend.sh`, `bash infra/ci/test-backend.sh unit`, `integration`, `browser`, and `bash infra/ci/test-frontend.sh`; see the [prerequisites/order/artifact policy](docs/ci.md). CI explicitly uses the repository's published container SDK10.0.404 in its disposable checkout; the committed Ubuntu development pin remains10.0.115. Only the existing client Prettier policy is gated. Images are built locally without registry push or deployment.

Build outputs go to ignored `artifacts/`, separated by project/configuration. To keep even local generated output outside the repository, pass the same `--artifacts-path /tmp/orderflow-artifacts` to restore/build/test/run (choose a writable equivalent on Windows). Use the same configuration/output path across commands; `--no-build` must refer to previously built output.

## Run the hosts separately

Start the API in one terminal:

```bash
dotnet run --project src/OrderFlow.Api --configuration Release --no-build --no-restore --no-launch-profile -- --urls http://127.0.0.1:5080
```

In another terminal:

```bash
curl --fail http://127.0.0.1:5080/api/health/live
```

For browser authentication, follow the [authentication setup and curl/browser runbook](docs/authentication.md): default cookies require HTTPS; an explicit Development-only flag permits loopback HTTP with separate cookie names.

Expected: HTTP 200 and `{"status":"Healthy"}`, with `X-Trace-Id`. This is process liveness only; no database/worker readiness is asserted. An unknown route returns sanitized ProblemDetails JSON. In Development, `/openapi/v1.json` exposes the generated OpenAPI 3.1 document; it returns generic 404 in Production. See [HTTP contract/examples](docs/http-contract.md) for status codes, input rules, ownership conventions and safe logs. Start the worker independently:

```bash
dotnet run --project src/OrderFlow.Worker --configuration Release --no-build --no-restore --no-launch-profile
```

Worker now processes due orders immediately and then every 5 seconds, up to 25 orders per pass. It needs private database configuration and the migrated schema to process work. API liveness remains independent of the worker/database. Set OrderExpiry__Enabled=false explicitly for an idle, service-free worker composition check. Press Ctrl+C for graceful shutdown; SIGTERM is also handled. See the [expiry runbook](docs/expiry-worker.md) for coordination, failure/restart behavior and configuration. The separate `seed-development` (roles) and `seed-development-account` (opt-in local account) commands are explicit and never run during normal startup. See the authentication guide for private account configuration.

## Configuration and environment variables

Each host's tracked `appsettings.json` is safe sample configuration: log levels/JSON console formatter, and API host-name filtering. Defaults contain no secrets/connection string. JSON logs use UTC timestamps; no request body/header/configuration dumping is enabled. API console scopes are disabled because framework scopes include raw RequestPath; TraceId remains an explicit safe log field. EF sensitive parameter logging/detailed errors and Npgsql connection error details are disabled; do not enable them with private data.

.NET host configuration reads `appsettings.json`, optional `appsettings.{Environment}.json`, environment variables, then command-line overrides. The normal `dotnet run --project ...` commands set the project's working directory so the correct settings are found. If launching the compiled DLL directly, set the working directory to that host's output directory (which includes its appsettings file).

| Setting | Use |
| --- | --- |
| `DOTNET_ENVIRONMENT` | Select host environment; default Production, optional Development for local work |
| `ASPNETCORE_URLS` | API listen URL; use loopback HTTP locally, for example `http://127.0.0.1:5080` |
| `AllowedHosts` | Semicolon-separated allowed API Host headers; defaults `localhost;127.0.0.1`; configure actual hostnames when a proxy is introduced |
| `Logging__LogLevel__Default` | Example nested setting override using `__` |
| `ConnectionStrings__OrderFlow` | Private PostgreSQL connection for either host/tools; never commit or print it |
| `ORDERFLOW_DB_PASSWORD_FILE`, `ORDERFLOW_DB_PORT` | Compose secret file and loopback port; see PostgreSQL guide |
| `Authentication__AllowInsecureHttp` | Default false; explicit loopback HTTP exception in Development only, rejected elsewhere |
| `DevelopmentSeed__Account__Enabled` | Default false; explicit Worker seed command only |
| `DevelopmentSeed__Account__Email`, `DevelopmentSeed__Account__Password`, `DevelopmentSeed__Account__Role` | Private development account configuration; Admin or Customer; no default email/password |
| `PaymentProcessing__Enabled`, `PaymentGateway__BaseUrl`, `PaymentGateway__ApiKey` | Opt-in Worker simulator transport; false by default, loopback URL/private key; Development/Testing only |
| `Simulator__Enabled`, `Simulator__ApiKey`, `Simulator__ControlKey`, `Simulator__CallbackSigningKey` | Optional simulator host; explicit dev/test enablement and independent private secrets; see payment runbook |
| `OrderExpiry__Enabled`, `OrderExpiry__BatchSize`, `OrderExpiry__PollIntervalSeconds` | Worker only; true / 25 (1–100) / 5 (1–300); seed commands suppress jobs |
| `ORDERFLOW_TEST_POSTGRES` | Optional private actual PostgreSQL administrator connection; overrides Testcontainers; fixture owns separate per-case databases |

For a shared environment value, prefer `DOTNET_ENVIRONMENT`; avoid setting a conflicting `ASPNETCORE_ENVIRONMENT`. [.env.example](.env.example) demonstrates non-secret local values. .NET does **not** load `.env` automatically. On Bash, optionally copy the example to ignored `.env`, review it, and run:

```bash
set -a
source .env
set +a
```

Keep passwords, connection strings with credentials, signing keys and certificates in private environment variables or a proper secret store. Do not put secrets in command-line arguments, tracked appsettings, fixtures, logs, or the example file. Ignored local JSON is not loaded automatically either. No dotenv/secrets-loading package is added. Compose itself reads .env by Docker convention; .NET does not. Default auth requires HTTPS; the documented development HTTP opt-in is local only. Same-origin proxy composition remains later work, and milestone 26 must implement shared protected Data Protection keys before multiple API replicas.

If a restricted environment cannot write the normal .NET CLI home, set `DOTNET_CLI_HOME` to a writable directory and optionally set `DOTNET_CLI_TELEMETRY_OPTOUT=1`. If the global NuGet cache is read-only, set `NUGET_PACKAGES` to a writable cache. These are local tooling paths, not application settings; never change the system `HOME` variable to work around permissions.

## Structure and dependencies

```text
OrderFlow.slnx
src/
  OrderFlow.Api/             HTTP host; references Application
  OrderFlow.Worker/          Expiry, payments and opt-in RabbitMQ publication
  OrderFlow.Simulator/       Restricted optional Development/Testing HTTP provider host
  OrderFlow.Application/
    Modules/                Domain rules and use cases, grouped by module/feature
    Persistence/            Scoped context, explicit mappings and migrations
    Messaging/              Durable outbox/inbox records, envelope and publisher claims
tests/
  OrderFlow.UnitTests/       Application money/time/pagination primitives
  OrderFlow.IntegrationTests/ HTTP, PostgreSQL and host composition tests
```

Application/Common contains BCL-only primitives. Persistence now contains OrderFlowDbContext/mappings/migrations, Modules/Identity contains Identity policy/storage and explicit development bootstrap, Modules/Catalog contains Product/Category rules/DTOs/use cases, Modules/Inventory owns balances/movements/stock commands and Active reservations, Modules/Ordering owns checkout/order snapshots/history, and Messaging contains durable outbox/inbox records. Other modules remain future boundaries. Domain/application/infrastructure are folder boundaries inside that assembly, as ADR-001 chose; they are not additional production projects. API/Worker do not reference each other, and Application references neither host. See [ADR-009](docs/adr/0009-postgresql-reliability-foundations.md).

## Dependency changes

`Directory.Packages.props` centrally pins direct package versions; each project has a generated packages.lock.json. EF Core/Design/Identity use 10.0.14, Npgsql EF/provider 10.0.3; the local dotnet-ef tool pins 10.0.14. Hosting/Mvc.Testing/OpenAPI stay 10.0.14, with Test SDK 17.14.1, xUnit 2.9.3 and VS runner 3.1.4 unchanged. `NuGet.config` declares only the official public feed and no credentials. Shared Application/Messaging infrastructure references RabbitMQ.Client 7.2.2 for Worker publishing/processing and the opt-in API hint consumer. API uses official SignalR Redis backplane 10.0.15; the native test client is 10.0.15. The test-only Testcontainers.PostgreSql module is pinned to 4.16.0. The design-time dependency is private; the API references it for EF's startup-project tooling, not a migration-on-startup feature.

For an intentional package upgrade, edit central versions, run `dotnet restore OrderFlow.slnx --force-evaluate`, inspect every changed lockfile and audit result, then repeat locked restore/build/test. Do not hand-author lockfiles, silently ignore restore failures, or disable vulnerability auditing to force a pass. `.editorconfig` defines formatting and `.gitignore` excludes generated outputs, private local settings, and certificate files; lockfiles and safe sample configuration remain tracked.

## Next milestone and limits

Milestone 13 completes [Customer order management and requested Admin fulfillment](docs/orders.md), pulling the graph/endpoint portion of 19 forward while leaving verified payment confirmation in 18. [ADR-016](docs/adr/0016-order-management-transitions.md) records order-first locking, read-only expiry views and the data-preserving OrderStatusHistory rename. Apply **20261105084550_AddOrderManagement** explicitly after building, coordinating schema/application versions for the renamed table. API/worker startup still never migrates. The [test harness](docs/testing.md) applies all twelve migrations only to owned databases.

Milestone 14 expiry slice implements [automatic expiry](docs/expiry-worker.md) with [ADR-017](docs/adr/0017-order-expiry-worker.md). Apply **20261106081044_AddSystemOrderExpiry** explicitly before enabling the worker; history actors may now be System/null user. Later slices below add settlement/refund recovery and RabbitMQ publication/notification consumers. Reads do not release overdue stock; owner cancellation safely finalizes expiry and returns the documented 409. Paid states reject customer cancellation. The current React foundation is documented below. No readiness endpoint exists yet; expiry is enabled by default; simulator payment processing is an opt-in Development/Testing loop. Status events commit as durable outbox intentions; deployment still needs shared protected Data Protection keys and least-privilege runtime roles. See [implementation status](docs/implementation-status.md) for exact checks and unverified fault scenarios.

Payment initiation/submission now ships with [ADR-018](docs/adr/0018-payment-initiation-and-simulator.md) and the [payment/simulator runbook](docs/payments.md). Apply **20261106084012_AddPaymentInitiationAndSimulator** explicitly. POST /api/orders/{id}/payment-attempts requires owning Customer, CSRF and Idempotency-Key, and persists Queued before any gateway call. Provider success is an unresolved observation, not order confirmation. Simulator requires explicit Development/Testing enablement, loopback binding and three independent private secrets; Production startup is forbidden. Signed callback receipt/settlement, stock consumption and merchant compensation intentions now ship with [ADR-019](docs/adr/0019-signed-payment-settlement.md). Apply **20261111040724_AddSignedPaymentSettlement** explicitly. See [callback runbook](docs/payment-callbacks.md) for private signing configuration, PaymentSettlement enablement, exact-body delivery and verification. Refund recovery and RabbitMQ publication are implemented by the slices below.

Late-capture recovery now includes [durable refund execution and payment reconciliation](docs/payment-recovery.md), under [ADR-020](docs/adr/0020-refund-execution-and-payment-reconciliation.md). Explicitly apply **20261111044314_AddRefundRecovery**, then enable PaymentReconciliation and RefundProcessing with the existing restricted gateway configuration. Refund keys/captures survive response loss/restart; lookup shares callback settlement and cannot revive expired orders. Pending/Succeeded/NeedsReview and provider-dependent external guarantees are documented; general customer refunds/returns and operator replay tooling remain out of scope.

RabbitMQ outbox publication now ships with [ADR-021](docs/adr/0021-rabbitmq-outbox-publication.md). Explicitly apply **20261112030213_AddOutboxPublishing**, prepare the private broker secret and start `docker compose --profile messaging up -d --wait rabbitmq`. Enable Worker `OutboxPublishing__Enabled=true` and privately supply `RabbitMq__Password`; follow [messaging](docs/messaging.md) for all configuration and recovery steps. Published means broker routing/confirmation, not consumer completion. Notifications/reporting and audited operator replay remain future work.

Durable owner notifications now ship with [ADR-022](docs/adr/0022-durable-user-notifications.md). Apply **20261112035709_AddNotifications** and enable Worker `Notifications__Enabled=true` with the existing private RabbitMq configuration. Consumer inbox + Notification + NotificationCreated outbox commit together before ACK. List/detail/unread/read APIs use the current cookie user and keep CSRF for read POST. See [notification setup/recovery](docs/notifications.md); realtime delivery remains future work; confirmation email is added by the slice below.

Order-confirmation email now ships with [ADR-023](docs/adr/0023-order-confirmation-email-delivery.md) and an [outage demo/runbook](docs/email-delivery.md). Explicitly apply **20261112082134_AddEmailDeliveries**, start `docker compose --profile email up -d --wait mailpit`, and opt into Worker EmailSending with existing Notifications/OutboxPublishing. Only loopback `.invalid` development email is allowed. SMTP recovery can duplicate accepted mail; stable Message-ID is not exactly-once delivery. Admin metadata/retry require role/CSRF and retain original identity.

Realtime notification backend now ships with [ADR-024](docs/adr/0024-signalr-notification-hints.md) and a [two-API demo/runbook](docs/realtime.md). Enable API Realtime with private Redis/RabbitMq config; Worker continues Notifications/OutboxPublishing and never owns a hub. Hints carry three stable IDs only, permit duplicates/loss, and require durable HTTP catch-up after reconnect. No database migration or frontend here. WebSockets with negotiation require proxy affinity; implement proxy configuration in 26.


Admin reports and read-only audit are documented in the [reporting runbook](docs/reporting.md): UTC-ranged paid sales, captured/refunded/net funds, status counts, top products, low stock and paginated safe audit. These direct PostgreSQL views need the additive read-index migration, an Admin cookie, and no reporting worker/projection. See [ADR-026](docs/adr/0026-direct-admin-reporting.md) for semantics and measured index choices.


The [React client runbook](docs/frontend.md) covers Node 24.19.0/npm 11.17.0, npm ci/build/dev, same-origin Vite proxy, real cookie/CSRF auth, scoped private cache, UI-only preferences and browser tests. Start the existing API with private DB/role setup first; client has no fake/default login. Default full solution tests now also require client dependencies + Playwright Chromium; use Category!=Browser for backend-only runs. Catalog/cart now ship under [ADR-028](docs/adr/0028-catalog-cart.md), including exact estimated totals and advisory price/stock review. Recoverable checkout/payment/order/notifications/live UI now ship under [ADR-029](docs/adr/0029-checkout-recovery-and-live-ui.md). Admin management/reports/audit and email-failure recovery now ship under [ADR-030](docs/adr/0030-admin-workspace.md). Production proxy remains later work.


Optional observability now ships under [ADR-031](docs/adr/0031-opentelemetry-and-readiness.md). Start only its local backend with `docker compose -f compose.telemetry.yaml --profile telemetry up -d --wait`; enable Telemetry and OTLP separately on API/Worker as described in the [runbook](docs/observability.md). API `/api/health/ready` checks PostgreSQL/schema, while `/api/health/live` remains process-only; Worker `check-readiness` additionally observes its enabled broker/SMTP dependencies. Exporters and optional delivery dependencies do not become API checkout admission gates. Generic failed-job/outbox/refund replay tooling and production composition/hardening remain deferred.
