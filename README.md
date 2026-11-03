# OrderFlow

A portfolio modular monolith for ASP.NET Core / full-stack development. Current delivery: milestone 04, Identity cookie authentication with registration/login/logout/current-user, explicit antiforgery validation, role policies and authentication rate limits. PostgreSQL persistence and shared reliability tables remain; the worker has no background jobs and offers explicit Development-only role/account seeding. CRUD and broker publishing are not implemented yet.

Read the [project contract](docs/project-spec.md), [architecture](docs/architecture.md), [ADRs](docs/adr/README.md), and [implementation status](docs/implementation-status.md) before adding features. Repository working rules are in [AGENTS.md](AGENTS.md).

## Prerequisites

- Install the **.NET SDK 10.0.114**, including its ASP.NET Core runtime, from the [official .NET 10 download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). It is a stable servicing release listed in [Microsoft's October 2026 announcement](https://github.com/dotnet/announcements/issues/496).
- `global.json` selects this exact SDK, disables preview selection and automatic SDK roll-forward. `dotnet --version` from the repository should print `10.0.114`. SDK upgrades are deliberate changes followed by restore/build/test, not machine-dependent substitutions.
- NuGet.org access is required on the first restore. Docker/Compose and actual PostgreSQL are required for the full integration suite; no payment provider, Node.js, or frontend is needed yet.

Follow the [PostgreSQL guide](docs/persistence.md) to generate a private password file, start the PostgreSQL-only Compose dev profile, export connection settings and run the **dedicated migration step**. The guide documents safe repeatable seeding, persistent volumes, inspection and manual reset. API/worker never migrate or seed automatically. Tests require ORDERFLOW_TEST_POSTGRES with CREATE DATABASE permission; use the documented filtered subset if PostgreSQL is unavailable.

## Restore, build, and test

Run from the repository root:

```bash
dotnet --version
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx --configuration Release --no-restore
# After PostgreSQL setup: export ORDERFLOW_TEST_POSTGRES="$ConnectionStrings__OrderFlow"
dotnet test OrderFlow.slnx --configuration Release --no-build --no-restore
```

The solution contains five projects. Current suite: 48 unit tests, 50 external-service-free HTTP/composition tests and 30 PostgreSQL integration tests, **128 total**. PostgreSQL tests create/migrate/drop only their own uniquely named test database, never the configured dev database/volume; missing configuration fails clearly rather than using an in-memory fallback. SQL tests cover actual uniqueness/FKs/checks, immutable snapshots, shared commit/rollback, UTC/numeric conversion and scoped host/Identity storage. Auth tests cover cookie/CSRF flows, roles, lockout, development configuration/seeding and deterministic duplicate-registration/two-host login races. Order ownership/business behavior remains future work. For the 98-test external-service-free subset, add `--filter 'Category!=PostgreSQL'`. Test-only controllers are explicitly registered in a test factory; the shipped API has no mutating probe endpoint.

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

Generic Host logs that the process started and waits. No jobs, polling loop, heartbeat simulation, or fake successful work are registered. Press Ctrl+C in each terminal for graceful shutdown; on Linux/container processes, SIGTERM is also handled by Generic Host. Neither host requires the other or a database connection to start. Resolving/using persistence requires the private connection setting. The separate `seed-development` (roles) and `seed-development-account` (opt-in local account) commands are explicit and never run during normal startup. See the authentication guide for private account configuration.

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
| `ORDERFLOW_TEST_POSTGRES` | Private test administrator connection; fixture owns separate databases |

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
  OrderFlow.Worker/          Generic Host; references Application
  OrderFlow.Application/
    Modules/                Domain rules and use cases, grouped by module/feature
    Persistence/            Scoped context, explicit mappings and initial migration
    Messaging/              Durable outbox/inbox records; no publisher yet
tests/
  OrderFlow.UnitTests/       Application money/time/pagination primitives
  OrderFlow.IntegrationTests/ HTTP, PostgreSQL and host composition tests
```

Application/Common contains BCL-only primitives. Persistence now contains OrderFlowDbContext/mappings/migrations, Modules/Identity contains Identity policy/storage and explicit development bootstrap, and Messaging contains durable outbox/inbox records. Other modules remain future boundaries. Domain/application/infrastructure are folder boundaries inside that assembly, as ADR-001 chose; they are not additional production projects. API/Worker do not reference each other, and Application references neither host. See [ADR-009](docs/adr/0009-postgresql-reliability-foundations.md).

## Dependency changes

`Directory.Packages.props` centrally pins ten direct package versions; each project has a generated packages.lock.json. EF Core/Design/Identity use 10.0.14, Npgsql EF/provider 10.0.3; the local dotnet-ef tool pins 10.0.14. Hosting/Mvc.Testing/OpenAPI stay 10.0.14, with Test SDK 17.14.1, xUnit 2.9.3 and VS runner 3.1.4 unchanged. `NuGet.config` declares only the official public feed and no credentials. The design-time dependency is private; the API references it for EF's startup-project tooling, not a migration-on-startup feature.

For an intentional package upgrade, edit central versions, run `dotnet restore OrderFlow.slnx --force-evaluate`, inspect every changed lockfile and audit result, then repeat locked restore/build/test. Do not hand-author lockfiles, silently ignore restore failures, or disable vulnerability auditing to force a pass. `.editorconfig` defines formatting and `.gitignore` excludes generated outputs, private local settings, and certificate files; lockfiles and safe sample configuration remain tracked.

## Next milestone and limits

Milestone 04 completes browser authentication; React scaffolding is next in 05. Read [ADR-010](docs/adr/0010-identity-cookie-authentication.md) for security choices and limits. No readiness endpoint, catalog/inventory/order/payment/client behavior or background job exists now. Shared reliability storage does not yet dispatch events or replay business commands. Full A01 includes the client/proxy later; A06/A16–A19 still need real use-case/dispatch/concurrency evidence beyond schema/transaction tests. Development owner privileges are not a production least-privilege deployment.
