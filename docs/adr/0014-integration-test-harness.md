# ADR-014: Per-case PostgreSQL databases and safe integration race boundaries

Date: 2026-11-05. Status: Accepted. Extends ADR-009/010/013; no business scope or migration change.

## Context and decision

The requested reusable harness follows completed Inventory 08 and is supplemental **08a**, depending on 08. Keep reservation milestone 09 and the existing roadmap IDs. Existing WebApplicationFactory/Identity helpers and SQL tests are adapted rather than replaced. The former collection-wide mutable test database is insufficient for independent, repeatable test cases.

Use test-only Testcontainers.PostgreSql **4.16.0**, locked centrally, and the same PostgreSQL 18.6 image/digest as Compose. One collection fixture owns its container; no reuse, fixed host ports or persistent volume. An explicitly supplied private ORDERFLOW_TEST_POSTGRES connection overrides container management and must support CREATEDB. Missing prerequisites cause failures with actual diagnostics; no automatic fake provider or success skip.

Every PostgreSQL fact/theory row derives from PostgresTest and receives a newly generated database with actual migrations. An optional migration target supports isolated upgrade tests. Disable pooling and impose explicit connection/SQL/lifecycle budgets. Drop only generated owned databases, clean up failed initialization and report cleanup errors. Leave Ryuk enabled as crash/timeout recovery; Testcontainers disposal has no cancellation argument, so its caller waits at most 30 seconds. Serial PostgreSQL collection execution bounds resource load; race participants use separate contexts/requests/hosts.

Keep the existing AuthApiFactory as the common HTTP host, with test-only DI replacements for UTC TimeProvider and interceptors, ephemeral keys and real cookie login/antiforgery. No production test routes, bypass handler, relaxed CSRF, rate-limit suppression or production startup migration. Allow only framework web-host/Data Protection startup hosted services; reject unknown hosted services before startup. Future workers require explicit removal and manual scoped iteration control in tests.

Unify SQL race helpers around a one-shot rendezvous keyed by distinct context IDs. Hook before the first common lock, or before independent writes whose earlier locks are on disjoint rows. Later matched statements from that context never wait again. Use real-time cancellation/timeout budgets and assert participant counts. In particular a stock INSERT barrier is safe for different-product collisions only; same-product requests synchronize before the balance lock. Test hooks orchestrate overlap; PostgreSQL remains the arbiter. Validate both HTTP responses and final durable balances/ledger, including the losing operation's lack of effect.

## Consequences and evidence

Per-case migrations cost time but avoid trigger-bypassing resets, hidden test order and ambient transaction differences. ManualTimeProvider changes business UTC time, not timers or framework Identity lockout internals. Representative-resource policy probes still do not claim real Order persistence coverage. Current tests also verify fixed-SKU isolation, bounded/idempotent cleanup with an open connection, logical timestamps/session expiry, unknown-loop rejection and absent production probes. See [testing guide](../testing.md) for commands/budgets/safe-boundary rules and [implementation status](../implementation-status.md) for exact run results.

Official references: [ASP.NET Core integration testing and ConfigureTestServices](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0), [Testcontainers PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/), [NuGet release 4.16.0](https://www.nuget.org/packages/Testcontainers.PostgreSql/4.16.0), [Testcontainers resource reaper](https://dotnet.testcontainers.org/api/resource_reaper/), [xUnit shared fixtures/collections](https://xunit.net/docs/shared-context), [PostgreSQL locking](https://www.postgresql.org/docs/18/explicit-locking.html).
