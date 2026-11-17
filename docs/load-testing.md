# Disposable failure and load testing

[ADR-034](adr/0034-disposable-failure-and-load-lab.md) defines the contract. Run only this harness's newly created, synthetic, localhost-only environment. There is no arbitrary base URL/connection-string option. Never adapt these commands to a public shop, production database or personal accounts. No payment or email service credentials are required. No production failpoints, schema migration, index or package changes are introduced.

## Reproduce

Prerequisites: repository .NET SDK10.0.115, Python3.12+, OpenSSL, a local Linux/WSL Docker daemon and Compose with access to the pinned image registries/NuGet. Container build SDK10.0.404 and all dependency images retain existing digest pins. The load generator is `grafana/k6:2.4.0` at the exact digest in [scenarios.json](../infra/load/scenarios.json); no host k6 installation/cloud account is needed. Allow roughly3.2GiB summed container memory caps plus host/build/test overhead. Use one command at a time and one lab on the fixed port/subnet.

```bash
# Run from the repository root. Keep this path for every command.
lab_dir="$PWD/.local/load-$(date -u +%Y%m%d%H%M%S)"
python3 infra/load/lab.py create --environment "$lab_dir"
python3 infra/load/lab.py up --environment "$lab_dir"
python3 infra/load/lab.py verify --environment "$lab_dir"
python3 infra/load/lab.py reads --environment "$lab_dir"
python3 infra/load/lab.py optimize --environment "$lab_dir"
python3 infra/load/lab.py correctness --environment "$lab_dir"
python3 infra/load/lab.py faults --environment "$lab_dir"
# A new output name is required; publisher works after containers stop, too.
python3 infra/load/report.py --environment "$lab_dir" --output docs/load-results/my-run.json
python3 infra/load/lab.py down --environment "$lab_dir"

# Service-free guard/report tests (also in the backend CI job):
python3 -m unittest discover -s infra/load -p 'test_*.py'
```

On the affected **snap Docker daemon only**, add `--snap-compat` to `create`. The existing [deployment compatibility explanation](deployment.md) applies: that override removes `no-new-privileges` locally because this daemon denies executable startup with it; it does not change the hardened default. Both recorded runs used this explicit override. A different local daemon should retain the default security option.

`up` requires an empty DB, builds the three actual images, starts PG/broker/Redis, runs explicit migrations/bootstrap, then starts both APIs/workers/nginx. Only localhost18446 is published. Create a new directory/project for a new run; never reuse credentials with unrelated volumes. This test operator also creates `pg_stat_statements` and a public run-marker table in its own DB, not an EF migration. Runtime application roles remain restricted. All business fixtures use real authenticated/antiforgery-protected HTTPS API calls. The controller refuses changed predeclared criteria, remote Docker overrides, foreign fixture accounts and noncanonical IDs.

On failure the process exits nonzero and preserves evidence; it does not choose a fallback target or silently rerun an uncertain checkout. Inspect the private failing phase log locally. Measured folders refuse reuse. Start a fresh lab for a complete write rerun. `down` removes only that project's containers/network, **retaining all volumes and private files**; never add `-v`, prune or delete old volumes. A partially seeded environment is not automatically reset. Running `up` again on a populated DB deliberately fails its fresh-data guard.

The empty Data Protection ring initially produced alternating401/antiforgery400 across replicas. Sharing a volume/certificate alone did not prevent that observed cold-start failure. The harness now materializes anonymous key state and recycles both API readers before login; four authenticated requests must then return200 from both nodes. A fresh-project rehearsal completed this bootstrap. The exact framework timing cause is unproven; this is a lab prerequisite, not a production rolling-start guarantee. Verify/initialize shared keys before admitting browser traffic, as discussed in [deployment](deployment.md). Ordinary API traffic remains round robin; auth/CSRF are enforced.

## Criteria fixed before execution

The fresh manifest copies [scenarios.json](../infra/load/scenarios.json) before starting services. A mismatch requires a new run. Threshold failures, empty/skipped/incomplete tests and broken invariants stop the run/publication.

| Scenario | Bound and success condition |
| --- | --- |
| Cache off/on | Four VUs,250 iterations each,90% product detail/10% category detail, all1,000 responses200, zero infrastructure/unexpected errors, p95<2,000ms diagnostic ceiling |
| Cache state | Twelve read products/one category;40-request prime. Delete only these13 generated catalog keys before cold/new prime. One cold read must query catalog; each measured warm run must query catalog DTOs zero times. Availability remains separately read from PG |
| Last units | Eight distinct authenticated customers each request1 unit from stock4; exactly four201/four409 `insufficient_stock`, both APIs observed, Reserved4 |
| Duplicate key | Eight concurrent requests, one user/exact payload/key, eight201 with identical body bytes, both APIs observed, one durable idempotency record/order/reservation |
| Broker outage | Stop only the owned RabbitMQ; readiness remains200 on both APIs, eight orders201, pending>=8; after broker startup pending0/failed0 within60s, record oldest age and recovery wall time |
| Fault suite | All nine selected cases pass, none skipped; actual PostgreSQL/broker/provider HTTP, original identity/dedup/lease/stock assertions and actual gate participants |
| DB invariants | No negative/over-reserved balances; Reserved equals active reservations and movement sums; OnHand equals movement sums; order total equals immutable line totals; checkout uniqueness |

There are15 products,one category,nine accounts (one generated Admin/eight Customers), USD12.50 prices,14 opening balances100 and the competing balance4. At most13 orders can be created by these phases. Read descriptions are about1KiB each. Writes use independent reserved SKUs; browsing does not change stock. k6 bounds: read4VUs/write8VUs (duplicate1VU issuing batch8), request5s, setup30s, scenario30s+5s grace, runner75s; no unbounded arrival-rate/saturation mode. Read iterations include10ms think time. Failure test restore/test deadlines are240s each, with serial fixture collections; no external `ORDERFLOW_TEST_POSTGRES` is accepted.

Fault datasets are fresh per case: the six business cases each create one order,one or two products with opening stock10 and Customer/Admin fixture accounts; the three outbox-only cases seed one outbox event each. Provider cases use a separate fresh DB and loopback Development/Testing simulator. Testcontainers have existing version/digest pins but **no explicit per-container CPU/memory quotas in this lane**. Precise HTTP distributions and sustained throughput for these fault interleavings are **unmeasured**. In-process scoped service instances are used for deterministic fault arbitration; the k6 lane separately uses actual API/worker processes.

## Recorded local measurements, 2026-11-17

Machine: Intel i5-13400,16 logical CPUs,16,133,856KiB reported memory, x86_64, kernel7.0.0-43-generic; Docker29.3.1/Compose5.1.1. Limits: API each.5CPU/512MiB; worker each1CPU/384MiB; PG1CPU/512MiB; Rabbit.5CPU/512MiB; Redis.25CPU/128MiB; nginx.25CPU/128MiB; k6.5CPU/128MiB. [Numeric artifact](load-results/2026-11-17.json) includes exact runtime image IDs, source revision/dirty status, durations, both-node counts, SQL deltas, errors and invariants. No fixture IDs/accounts/credentials or raw logs are published.

The final controlled read comparison reused the fresh second lab's unchanged12 read products after its correctness run; DB buffers were warm. Cache-off was primed; cache-on cold means the13 Redis keys were deleted, not a cold disk/PG buffer pool. Each warm phase starts with a fresh key reset/40-request prime and verifies zero catalog DTO queries during measurement. No other fault suite ran concurrently with these final reads. Business bursts below came from the same lab's earlier serial full run. These are individual sequential observations, not averages or confidence intervals.

| Scenario | n | p50 ms | p95 ms | p99 ms | Request/s | k6 window s | Outcome |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Cache off, warmed |1,000|4.42|16.82|39.26|232.61|4.30|1,000×200 |
| Cache on, one cold probe |1|564.70|564.70|564.70|1.72|0.58|1×200; percentiles/RPS not capacity evidence |
| Cache on, warmed, SQL Information |1,000|5.68|69.30|89.73|129.00|7.75|1,000×200 |
| Same warmed mix, SQL Warning |1,000|7.26|58.70|87.25|124.86|8.01|1,000×200 |
| Eight Customers/last four units |8|1,216.60|1,289.11|1,292.27|2.42|3.30|4×201,4 expected409 |
| Eight identical checkout requests |8|316.48|367.28|368.98|16.13|0.50|8×201; one order |
| Broker stopped, new checkouts |8|49.74|139.46|162.66|9.31|0.86|8×201 |

Every row had **zero unexpected, transport,5xx, authentication/throttle or validation errors**. Expected stock409s are counted separately from infrastructure errors. Both APIs each handled500 of each1,000-read phase and4 of each8-write burst. Tiny write samples have descriptive tails only. HTTP duration excludes DNS/connect/TLS setup; counter throughput uses the complete k6 window, including login/token setup for write bursts. Runner/container setup wall time is separately recorded; these rates are not an open-arrival capacity result.

Observed DB query reduction: Information-level EF command events went from **1,000 to902** for the same1,000-read mix; readiness probes can add events. `pg_stat_statements` catalog-containing calls went from1,000 to0 in the warmed cache run. This does **not** mean zero DB reads:900 product requests still read mutable availability, while100 category hits avoid their read; SQL totals also contain transaction/control/readiness traffic. Quiet logging exported0 command log events while PostgreSQL still recorded1,803 total statement calls. The cache did not improve HTTP latency/throughput in this environment; two Redis lookups plus fresh availability and shared local resources can cost more than a small indexed projection. No fabricated improvement percentage is used.

Broker outage: pending13/failed0, oldest age3.123s at the post-burst observation; after restart, pending0/failed0/age0 in **9.655s**, including broker startup. The first lab recovered in18.794s with oldest age7.110s, illustrating variance. Final DB snapshot had13 orders/15 products/9 users and zero stock, money or dedup violations. These are bounded point observations, not continuous backlog monitoring or an unlimited buffering guarantee.

| Deterministic fault boundary | Measured recovery wall ms | Proven condition |
| --- | ---: | --- |
| Callback versus cancellation |63.25|Two actual participants, Cancelled, one stock effect/unique compensation |
| Callback versus expiry |219.60|Two actual participants, Expired at logical expiry boundary, no duplicate allocation |
| DB commit before ACK/channel restart |39.35|Real redelivery; one inbox/notification/child outbox |
| Refund response lost after success |100.25|One provider refund, same durable identity; lookup/restart recovery |
| Same plus lost capture response |199.30|One provider refund, same identity despite two uncertainties |
| Outbox stale lease owner |782.13|Two owners, attempts2, stale completion cannot overwrite newer publication |
| Refund stale lease owner |154.03|Two owners, delivery2, provider effect1 |

Nine cases passed/zero failed/skipped in35.30s including restore/build/fixtures; the other two cases assert independent-worker claim exclusivity and publish-confirm-before-SQL-ack recovery with permitted duplicate delivery. No HTTP p50/p95/p99/RPS is inferred from these Stopwatch samples. Callback timing includes the contested operations and invariant reconciliation; lease timing starts with the first operation in flight and includes arrival at its pause/reclaim/assertions. ACK timing starts after channel disposal; refund lookup timing starts after the original1s HTTP timeout and excludes that initial failed request. Retry/lease5/60s and expiry900s are logical clock advances, not real waiting/recovery durations. Stop is simulated at exact boundaries using test-only ACK exception/channel disposal, SQL failure and TCS/SQL gates; an OS process kill at the exact post-commit boundary is **unmeasured**.

## Observed bottleneck and rerun

Initial worker caps.25CPU caused existing CLI readiness DB probes to time out and both workers to become unhealthy. Cgroup evidence showed worker1 throttled764 of947 periods and worker2 738 of970; main loops still issued SQL. Increasing only the load overlay worker cap to1CPU made both healthy on the repeated probe,7.829s from recreation to healthy. The second fresh lab also completed worker startup with that cap. This supports resource starvation as the bottleneck; it does not prove a specific EF method or justify longer correctness timeouts/index changes. Application semantics and the base deployment profile were preserved.

A second safe experiment lowered EF command logs from Information to Warning after observing902 events per cached1,000 reads. The final table shows the same TTL-controlled mix rerun: p95/p99 decreased slightly, p50 and throughput worsened. **No reliable latency improvement is claimed.** An earlier primed run had21 catalog refill queries because hits did not renew absolute TTL; it was retained privately and excluded from the controlled result. The harness now explicitly resets only fixture keys and fails a “warm” measurement if catalog refills occur. A resumed read attempt also correctly stopped before measurement when Redis had not been started; it was restored explicitly. These failures were not reclassified as cache wins or successful runs.

## Limits and evidence

Private evidence is under `.local/load-20261117-a` and `-b`: phase logs, summaries/results, hardware, marker/settings, fault TRX/public summary/timings and startup before/after cgroup probes. Files/volumes remain; only owned containers/networks were stopped. First lab had an initial eight-case exploratory fault selection, then the complete predeclared nine-case suite; final lab ran all nine. A duplicate `up` invocation refused the populated fixture DB instead of resetting it. Raw evidence may contain generated auth details; inspect locally and do not upload it.

Use [report.py](../infra/load/report.py) to publish numeric/allowlisted data only. Its guard tests reject remote targets, changed bounds, foreign accounts/SQL-like IDs, broken invariants, nonfinite measurements, and private arbitrary fields. A missing dependency, incomplete result, failed threshold or skipped case remains failure. The existing GitHub workflow now runs these seven service-free tests; full load/outage runs remain explicitly provisioned local commands. **This milestone's GitHub workflow was not executed remotely**, and the full frontend/full integration suite was not rerun for these test/tool/doc-only changes.

One machine shares CPU scheduling, page cache, disk, network bridge and daemon overhead across clients/services. Generator quota, background health probes, JIT/GC, prior cache age and single samples affect tails. There is no WAN, real provider/email, multi-host partition, prolonged saturation, production-data skew, HA or production SLO evidence. Do not multiply local RPS by replica count or interpret correctness at eight requests as proof of unlimited scale.
