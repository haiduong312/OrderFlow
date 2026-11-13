# Optional catalog detail caching

Implemented scope/consistency: [ADR-025](adr/0025-bounded-catalog-cache.md). Existing [catalog routes/security](catalog.md) are unchanged. Enable only where repeated category reads/catalog projection savings justify Redis overhead; it is disabled by default.

## Setup and manual verification

Use the existing optional Redis Compose service (same pinned Redis 8.10.3 image as realtime, separate cache key namespace). Follow [persistence setup](persistence.md) for the private DB connection, password-file environment and explicit migration command first. Do not put real Redis credentials in shell history or source; configure credentialed connection strings through the same private environment/secret mechanism.

```bash
docker compose --profile dev up -d redis
export CatalogCache__Enabled=true
# Loopback-only development Redis has no password; production requires private auth/TLS configuration.
export CatalogCache__RedisConnectionString=127.0.0.1:6379
export CatalogCache__RedisPrefix=orderflow:local:catalog:
dotnet run --project src/OrderFlow.Api --no-launch-profile -- --urls http://127.0.0.1:5080
```

For plain HTTP browser authentication, use the existing Development-only Authentication__AllowInsecureHttp opt-in. Cache-enabled startup validates connection syntax/prefix, but does not connect until a detail read/mutation. An unreachable Redis should not prevent API startup or a DB-backed response. Realtime remains separately configured; enabling this cache does not enable SignalR/consumers.

1. Obtain an active category/product UUID through `/api/categories` and `/api/products`. Request `/api/categories/{id}` repeatedly. Observe EF SQL and the cache meter: first read misses/SELECTs, later reads hit. Request product detail repeatedly: live inventory SELECT still occurs on every read. Public price remains a string such as `"12.50"`.
2. Start a second API in another terminal on port 5081 with the same DB/Redis/prefix. Warm a detail on 5081; update/archive/restore through the existing Admin cookie+CSRF endpoints on 5080 with expectedVersion. Sequential reads on 5081 see changes after successful invalidation; no L1 needs eviction. Product category rename/archive/restore works through the separate category key. Admin reads and filtered lists always query DB.
3. Stop Redis with `docker compose --profile dev stop redis`. Read details and mutate catalog: PostgreSQL still serves/persists them, warning 2501 identifies `get`/`remove` failure. Restart with `docker compose --profile dev start redis`. Existing entries after a failed DELETE can stay stale until their original deadline if the Redis deployment retains them. This project's ephemeral Redis container may instead lose keys on restart; tests separately model retained keys plus failed DELETE.
4. Inspect meter `OrderFlow.CatalogCache` / `catalog_cache_operations` using an OpenTelemetry-compatible collector or `dotnet-counters` if installed. No telemetry/exporter package is added here. Warning 2502/invalid_payload means the cache value was rejected and DB was read. Logs omit cache values and raw dependency exceptions.

Keys are prefix + `catalog:v1:product:{lowercase-uuid-without-hyphens}` or `catalog:v1:category:{...}`. Both have 30-second absolute TTL and embedded source-read deadlines; values are UTF-8 JSON <=16 KiB. No sliding refresh, negative entries, filtered-list keys, local memory, private data or availability entries. Source-read start, not SET completion, anchors the deadline. TTL assumes synchronized UTC host clocks; in-flight responses can finish later. See ADR for stale-refill/crash races and transaction boundaries.

## Executable tests and query measurement

Requires Docker daemon access/pull permission; fixtures own disposable PostgreSQL and Redis, with no RabbitMQ/SMTP requirement. Tests use generated per-case DB/key prefixes and never delete developer keys/data.

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet test OrderFlow.slnx -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~CatalogCache' --logger 'console;verbosity=normal'
```

`Repeatable_ten_category_reads_measure_exact_SQL_reduction_and_lists_stay_uncached` starts independent disabled/enabled hosts and counts actual EF SELECTs over ten sequential category-detail HTTP requests to the same row: **10 disabled, 1 enabled (1 miss + 9 hits)**. It also checks that two category list reads still execute four SELECTs (count+page). This is a deterministic statement-count demonstration, not a percentage, throughput or latency benchmark. The product hit/miss test separately asserts 3 SELECTs on cold miss and 1 live-inventory SELECT on a cross-node hit.

The race test counts the reader reaching the post-SELECT/pre-SET gate, commits update/archive and verifies DELETE before releasing it. Logical +30 seconds tests the exact payload deadline; it does not sleep for Redis TTL. The invalidation-failure test checks EF transaction disposal before the failing removal, old shared data before expiry, fresh Admin bypass and fresh public detail at expiry. Actual Redis outage tests stop/start the fixture and check safe warning fields plus HTTP 500 for injected SQL failure.

Remaining trade-offs: cold product reads add SQL round trips; no distributed stampede protection, atomic product/category snapshot, durable invalidation, OS-kill/cluster/partition proof, production Redis auth/TLS/ACL/memory policy or latency benchmark. Checkout, stock, payment, auth, account/order reads and filtered lists do not use this cache. There is no new migration.
