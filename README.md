# OrderFlow

A portfolio modular monolith for ASP.NET Core / full-stack development. Current delivery: milestone 02a, shared HTTP/application foundations after the initial solution. API liveness, consistent errors/traces/logging, typed USD money, UTC time helpers, pagination validation and Development OpenAPI are implemented. Worker still waits for shutdown with no jobs. CRUD/business/database/authentication features are not implemented yet.

Read the [project contract](docs/project-spec.md), [architecture](docs/architecture.md), [ADRs](docs/adr/README.md), and [implementation status](docs/implementation-status.md) before adding features. Repository working rules are in [AGENTS.md](AGENTS.md).

## Prerequisites

- Install the **.NET SDK 10.0.114**, including its ASP.NET Core runtime, from the [official .NET 10 download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). It is a stable servicing release listed in [Microsoft's October 2026 announcement](https://github.com/dotnet/announcements/issues/496).
- `global.json` selects this exact SDK, disables preview selection and automatic SDK roll-forward. `dotnet --version` from the repository should print `10.0.114`. SDK upgrades are deliberate changes followed by restore/build/test, not machine-dependent substitutions.
- NuGet.org access is required on the first restore. No database, Docker, payment provider, Node.js, or frontend setup is required for this milestone.

## Restore, build, and test

Run from the repository root:

```bash
dotnet --version
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx --configuration Release --no-restore
dotnet test OrderFlow.slnx --configuration Release --no-build --no-restore
```

The solution contains five projects. UnitTests runs 37 tests for money, UTC time/expiry precision and pagination; IntegrationTests runs 49 HTTP/composition tests including error mapping, validation, traces/redaction, request cancellation, JSON money, the configured JSON console formatter and OpenAPI. The two original host composition smoke tests remain. Counts reflect milestone 02a. There is no claim of PostgreSQL/ownership/business integration coverage. ASP.NET TestServer exercises the real HTTP pipeline, not an EF database substitute. Probe controllers are explicitly registered in the test factory only; the shipped API has no mutating probe endpoint.

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

Expected: HTTP 200 and `{"status":"Healthy"}`, with `X-Trace-Id`. This is process liveness only; no database/worker readiness is asserted. An unknown route returns sanitized ProblemDetails JSON. In Development, `/openapi/v1.json` exposes the generated OpenAPI 3.1 document; it returns generic 404 in Production. See [HTTP contract/examples](docs/http-contract.md) for status codes, input rules, ownership conventions and safe logs. Start the worker independently:

```bash
dotnet run --project src/OrderFlow.Worker --configuration Release --no-build --no-restore --no-launch-profile
```

Generic Host logs that the process started and waits. No jobs, polling loop, heartbeat simulation, or fake successful work are registered. Press Ctrl+C in each terminal for graceful shutdown; on Linux/container processes, SIGTERM is also handled by Generic Host. Neither host requires the other to start.

## Configuration and environment variables

Each host's tracked `appsettings.json` is safe sample configuration: log levels/JSON console formatter, and API host-name filtering. Defaults contain no secrets or external-service configuration. JSON logs use UTC timestamps; no request body/header/configuration dumping is enabled. API console scopes are disabled because framework scopes include raw RequestPath; TraceId remains an explicit safe log field. No database or signing setting is consumed yet.

.NET host configuration reads `appsettings.json`, optional `appsettings.{Environment}.json`, environment variables, then command-line overrides. The normal `dotnet run --project ...` commands set the project's working directory so the correct settings are found. If launching the compiled DLL directly, set the working directory to that host's output directory (which includes its appsettings file).

| Setting | Use |
| --- | --- |
| `DOTNET_ENVIRONMENT` | Select host environment; default Production, optional Development for local work |
| `ASPNETCORE_URLS` | API listen URL; use loopback HTTP locally, for example `http://127.0.0.1:5080` |
| `AllowedHosts` | Semicolon-separated allowed API Host headers; defaults `localhost;127.0.0.1`; configure actual hostnames when a proxy is introduced |
| `Logging__LogLevel__Default` | Example nested setting override using `__` |

For a shared environment value, prefer `DOTNET_ENVIRONMENT`; avoid setting a conflicting `ASPNETCORE_ENVIRONMENT`. [.env.example](.env.example) demonstrates non-secret local values. .NET does **not** load `.env` automatically. On Bash, optionally copy the example to ignored `.env`, review it, and run:

```bash
set -a
source .env
set +a
```

Keep future passwords, connection strings with credentials, signing keys and certificates in private environment variables or a proper secret store. Do not put secrets in command-line arguments, tracked appsettings, fixtures, logs, or the example file. Ignored local JSON is not loaded automatically either. No dotenv/secrets-loading package or unused provider configuration is added here. HTTPS/reverse-proxy browser deployment remains a later milestone; local loopback HTTP proves host startup only.

If a restricted environment cannot write the normal .NET CLI home, set `DOTNET_CLI_HOME` to a writable directory and optionally set `DOTNET_CLI_TELEMETRY_OPTOUT=1`. If the global NuGet cache is read-only, set `NUGET_PACKAGES` to a writable cache. These are local tooling paths, not application settings; never change the system `HOME` variable to work around permissions.

## Structure and dependencies

```text
OrderFlow.slnx
src/
  OrderFlow.Api/             HTTP host; references Application
  OrderFlow.Worker/          Generic Host; references Application
  OrderFlow.Application/
    Modules/                Domain rules and use cases, grouped by module/feature
    Persistence/            Future shared context, schema mappings and migrations
    Messaging/              Future durable outbox/inbox infrastructure
tests/
  OrderFlow.UnitTests/       Application money/time/pagination primitives
  OrderFlow.IntegrationTests/ HTTP foundations + host composition tests
```

Application/Common contains small BCL-only primitives (Errors, Money, Time, Pagination); modules/persistence/messaging remain future boundaries. Domain/application/infrastructure are folder boundaries inside that assembly, as ADR-001 chose; they are not additional production projects. API/Worker do not reference each other, and Application references neither host. See the [scaffold decision](docs/adr/0007-solution-bootstrap.md) and [foundations decision](docs/adr/0008-http-application-foundations.md).

## Dependency changes

`Directory.Packages.props` centrally pins six direct package versions; each project has a `packages.lock.json` containing the resolved graph/content hashes. The worker uses `Microsoft.Extensions.Hosting` 10.0.14; HTTP tests use `Microsoft.AspNetCore.Mvc.Testing` 10.0.14; OpenAPI uses `Microsoft.AspNetCore.OpenApi` 10.0.14; the stable VSTest/xUnit baseline uses Test SDK 17.14.1, xUnit 2.9.3, and VS runner 3.1.4. API uses the SDK's ASP.NET Core shared framework; EF/Npgsql are intentionally not installed yet. `NuGet.config` declares only the public official feed; there are no feed credentials.

For an intentional package upgrade, edit central versions, run `dotnet restore OrderFlow.slnx --force-evaluate`, inspect every changed lockfile and audit result, then repeat locked restore/build/test. Do not hand-author lockfiles, silently ignore restore failures, or disable vulnerability auditing to force a pass. `.editorconfig` defines formatting and `.gitignore` excludes generated outputs, private local settings, and certificate files; lockfiles and safe sample configuration remain tracked.

## Next milestone and limits

The added milestone 02a precedes the original 03–29 roadmap without renumbering it. Milestone 03 still adds actual PostgreSQL setup, the shared scoped context and initial migrations/test harness. No authentication, database readiness endpoint, migration, catalog, inventory, order, payment, client or future job is present now. Full A01 also includes the client and same-origin deployment in later milestones. Full A03/A08/A22 also require business/SQL/operational evidence beyond the current primitives and HTTP tests.
