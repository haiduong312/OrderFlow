# API and Worker observability

[ADR-031](adr/0031-opentelemetry-and-readiness.md) defines instrumentation, redaction, sampling and readiness. No migration is added; preserve all existing explicit migration/role/secret provisioning. Telemetry is optional and is not a correctness/audit dependency.

[ADR-032](adr/0032-replicated-compose-deployment.md) adds a separate [deployment telemetry overlay](deployment.md) for API/Worker containers. Explicit AllowContainerHost permits exactly private http://telemetry:4318; production external targets still require HTTPS. No app depends_on the collector. The host-only standalone backend below stays available independently. Optional API realtime dispatch suspension and fatal-worker exit codes now make dependency admission/restart policy explicit without treating readiness as progress.

## Run the optional local backend

```bash
docker compose -f compose.telemetry.yaml --profile telemetry up -d --wait
```

This standalone profile requires no main Compose PostgreSQL password file. The pinned Grafana LGTM0.41.0 image includes OTLP Collector, Prometheus, Tempo, Loki and Grafana. Initial pull is substantial; memory is capped2GiB/2CPU. Loopback-only Viewer UI: `http://127.0.0.1:3000/d/orderflow-operations`. OTLP HTTP: `http://127.0.0.1:4318/`; no remote telemetry credential is supplied. Configure optional host ports with ORDERFLOW_TELEMETRY_UI_PORT / ORDERFLOW_OTLP_HTTP_PORT. The read-only dashboard JSON and provider live in infra/telemetry. This demo profile uses container-local ephemeral data; down/recreation can lose observations. Do not treat it as an audit store.

After the normal private PostgreSQL/authentication setup, run the existing hosts in separate terminals:

```bash
# Existing DB connection and all private dependency configuration must already be supplied.
Telemetry__Enabled=true Telemetry__OtlpEnabled=true \
  Telemetry__Endpoint=http://127.0.0.1:4318/ \
  DOTNET_ENVIRONMENT=Development Authentication__AllowInsecureHttp=true \
  dotnet run --project src/OrderFlow.Api --no-launch-profile -- --urls http://127.0.0.1:5080
```

```bash
Telemetry__Enabled=true Telemetry__OtlpEnabled=true \
  Telemetry__Endpoint=http://127.0.0.1:4318/ \
  DOTNET_ENVIRONMENT=Development dotnet run --project src/OrderFlow.Worker --no-launch-profile
```

Opt into OutboxPublishing/Notifications and privately configure RabbitMQ as in [messaging](messaging.md)/[notifications](notifications.md); otherwise checkout events will intentionally remain pending. Other payment/email flags and secrets retain their existing runbooks. Starting the worker does not opt into gateways/email or run migrations. For local email use [email delivery](email-delivery.md). Frontend checkout is described in [frontend](frontend.md).

Configuration:

| Setting | Default / effect |
| --- | --- |
| Telemetry:Enabled | false; enable SDK and independent operational sampling |
| Telemetry:OtlpEnabled | false; when master Enabled, export traces/metrics/logs asynchronously |
| Telemetry:Endpoint | loopback HTTP4318 base URL; HTTPS or loopback HTTP only, no credentials/query/fragment/path |
| Telemetry:TraceSampleRatio |1; parent-based ratio0–1, local full sampling; configure lower ratios deliberately for other environments |
| Telemetry:SnapshotIntervalSeconds |15; accepted5–300; 2s SQL statement and5s overall sample deadlines |

Master off means no SDK exporter or sampling DB polling. SDK instrumentation can be enabled with export off. Export enabled with an unreachable endpoint still starts; batch queues are bounded and may drop telemetry. Fix/exporter restart does not reconstruct lost spans. Health excludes OTLP. No raw data/body/header/SQL/exception-message switch is offered.

## Follow an order

Create a real order through authenticated browser checkout. Record the response `X-Trace-Id` or ProblemDetails traceId. In Grafana Explore → Tempo search that trace ID (or service.name=orderflow-api, span `checkout.create`). After worker publication/notification processing, the same trace contains `outbox.publish` and `notifications.handle`, plus HTTP/PostgreSQL child spans. `messaging.message.id` and `orderflow.correlation_id` allow comparison with durable outbox/audit IDs. Publisher retries keep the immutable producer envelope, so publish/consume appear as siblings below the producer. Notification-created work uses its consumer context for the next event. Native context is not authorization and event redelivery can add spans without another notification effect.

Trace sampling, older events without context, independently initiated callbacks/payment work, unavailable exporters or lost telemetry can leave partial trees. Use durable admin order/audit history as the source of truth. Traces cannot prove successful payment/refund/consumer delivery. Notification handling span ends on DB commit; ACK failure is logged/counted separately and allows safe redelivery.

Grafana Explore → Loki can filter `{service_name="orderflow-worker"}` and `|= "OutboxPublishFailed"`; exported log metadata includes trace IDs, fixed codes and safe operation fields. Consumer acknowledgement logs recover their trace ID from the validated envelope after the scoped DB activity has ended. Console JSON uses the same safe fields/event names. SQL, connection strings, arbitrary formatted messages, full exceptions, cookies/tokens/signatures, payment payloads and email recipient/body/provider keys are omitted. Unexpected failures remain visible through exception type, error status, classified counter, code and readiness/backlog. There is intentionally less raw framework text; do not turn on a secret-bearing debug exporter to compensate.

## Dashboard metrics

All labels are bounded: outcome={success,rejected,server_error,cancelled}; component={Outbox,Notifications,Realtime,Email}; kind={Dependency,InvalidMessage,Unexpected,LeaseLost}; dependency={postgresql,rabbitmq,smtp}. No business identity/URL/raw error/queue labels. SDK limits each stream to100 regular combinations plus one overflow point; checkout histogram permits8 regular combinations plus overflow and is outcome-only. Zero-dimension points are also outside that SDK budget; existing instruments use either their fixed labels or no labels. Service resources identify processes/services independently of orders/users.

| Prometheus series | Meaning |
| --- | --- |
| orderflow_checkout_duration_seconds_bucket/count/sum | Whole checkout HTTP duration in seconds, including rejection/replay; histogram quantiles need observations |
| orderflow_checkout_requests_total{outcome} | Checkout HTTP request outcomes; rejected4xx differs from server_error5xx/cancellation |
| orderflow_processing_failures_total{component,kind} | Failed delivery/processing attempts, including classified NACK/ACK/lease loss; can count redeliveries more than once |
| orderflow_outbox_publications_total{outcome} | Broker-confirmed/failed publication attempts; confirms do not imply consumer completion |
| orderflow_outbox_pending | All undelivered Pending+InFlight, including scheduled retries, leases and exhausted work awaiting cleanup |
| orderflow_outbox_oldest_age_seconds | Age of oldest undelivered OccurredAt at sample time;0 only after a successful sample found none |
| orderflow_outbox_failed | Terminal Failed work requiring review |
| orderflow_expiry_due / orderflow_expiry_oldest_lag_seconds | Due PendingPayment orders (not number of item reservations) and oldest unprocessed expiry lag, including equality boundary |
| orderflow_reservation_expiry_lag_seconds_bucket/count/sum | Successfully committed background expiry lag, measured at locked decision time; separate from outstanding work |
| orderflow_refunds_pending / orderflow_refunds_review | Pending confirmation/lease/retry/uncertainty vs NeedsReview; succeeded refunds excluded |
| orderflow_telemetry_sample_success / orderflow_telemetry_snapshot_age_seconds | Failure keeps last values with success0 and increasing age; before first success backlog is absent |
| orderflow_dependency_ready / orderflow_runtime_ready | Last sampled dependency/runtime readiness; see policy below |

Existing bounded catalog cache counters are also collected. Gauges are observations from separate bounded statements, not an atomic financial report; statements may see concurrent changes. Multiple API/worker processes can sample the same DB: dashboard uses MAX for backlog/age and MIN for dependency readiness, not a sum that double-counts work. Counters/rates sum across actual processes. The outbox chart uses one-minute rolling maxima so short backlog spikes remain visible; its visual recovery lags the raw gauge by up to one minute. An untouched/stale/missing exporter/dashboard sample never means an empty backlog. Current count scans may still be expensive at scale; inspect plans and adjust interval/budgets before adding indexes. No speculative index/migration is included.

Examples to discuss, not production guarantees/SLOs: checkout server_error fraction>1% for5m with sufficient traffic; oldest outbox age>60s for5m or continuously growing count; oldest due expiry lag>30s for5m; any Failed outbox/NeedsReview refund; pending refunds not draining over an operator-chosen window; sample success0, sample age>2 intervals, missing ingestion or runtime readiness0. Low traffic can make p95/rates misleading; zero traffic leaves no error fraction. The dashboard does not install production alerts or page anyone.

## Liveness and readiness

```bash
curl --fail http://127.0.0.1:5080/api/health/live
curl --fail http://127.0.0.1:5080/api/health/ready
# With the same existing private Worker configuration:
dotnet run --project src/OrderFlow.Worker --no-launch-profile -- check-readiness
```

API liveness is process responsiveness only. API readiness checks PostgreSQL connectivity plus pending migrations against this binary (2s budget), returns200/Healthy or503/Unhealthy and no-store, never connection strings/migration details/DDL. Migration-history readiness does not prove every table/privilege/capacity invariant or guarantee the next transaction. Stopping PostgreSQL makes readiness fail; liveness can remain200.

API readiness intentionally ignores RabbitMQ, SMTP, Redis, gateway and telemetry. Existing transactions commit an outbox intention and do not make network calls in checkout, so broker/mail downtime can grow backlog without rejecting all checkout. Storage is not infinite; monitor age/count/failures/capacity. Cache/hub availability is separate from database authority.

Worker has no public HTTP listener. Its internal probe/CLI returns safe dependency booleans and exit0/1; gauges are sampled only when telemetry is enabled. PostgreSQL/schema is required; RabbitMQ only when publisher/notifications jobs enabled; SMTP TCP reachability only when EmailSending enabled. Broker handshake does not verify topology/consumer progress; SMTP TCP does not verify mail delivery. Disabled features add no dependencies. Gateway/telemetry are excluded. Existing finite reconnects/fatal worker failures remain intact; a process may be alive while unready, or exit after exhausted retries. CLI observes dependencies, not the health of some separate already-running Worker PID; combine it with that process's liveness and backlog progress.

## Repeatable fault and ingestion verification

Core default tests do not require LGTM. The telemetry integration cases use actual PostgreSQL/RabbitMQ and SDK capture; normal tests fail on missing required prerequisites, without success skips. To additionally verify this optional profile's actual Prometheus/Tempo/Loki ingestion:

```bash
docker compose -f compose.telemetry.yaml --profile telemetry up -d --wait
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
ORDERFLOW_TELEMETRY_VERIFY_BASE=http://127.0.0.1:3000 \
ORDERFLOW_TELEMETRY_VERIFY_OTLP=http://127.0.0.1:4318/ \
  dotnet test OrderFlow.slnx -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Observability'
```

The test creates isolated DBs/users/orders, records real pending1→2→0 through broker failure/recovery and actual consumer commits, and waits on observed ingestion/ACK gates. It separately sends real rejected/DB-failing checkout requests and checks finite outcome series, tests overdue equality/+9s lag and late-capture Pending→NeedsReview refunds, checks schema/dependency policy, and starts API/Worker against unavailable OTLP. An actual PostgreSQL error contains a synthetic private marker; exported span/log assertions reject that marker and SQL. No fake successful API/payment response or arbitrary race sleep establishes correctness. External backend assertions run only when the explicit verification base is configured; test output says whether they ran.

Evidence under `/tmp/orderflow-telemetry/{results,evidence}` includes safe order trace, Tempo trace, Loki failure and final dashboard screenshot/query results when generated. The Grafana dashboard can query the test service `orderflow-verification`; normal hosts remain orderflow-api/orderflow-worker. The same-process SDK test proves persistence/transport lineage with no inherited HTTP Activity; the manual two-process setup above supplies separate production service resources. No production load/availability guarantee or deployed collector security/retention is claimed.

Safe manual fault demo: on a disposable private dev environment, stop only its broker, create orders and observe pending count/age growth while API readiness remains200. Restart it; watch publication/notifications drain the backlog. Stop mail sink and inspect Worker readiness/error/counter state without changing payment/order facts. Stop PostgreSQL: API readiness503/liveness200 until host policy dictates otherwise. Stop telemetry alone: API/Worker must start and business work continues, while dashboards go stale. Use existing same-identity recovery runbooks for terminal work; do not edit SQL/state/counters to make telemetry green.

To stop the telemetry profile you started, use `docker compose -f compose.telemetry.yaml --profile telemetry down`. This removes only that profile's container/network, not the main app's DB/broker/mail volumes. All telemetry storage here is disposable demo data.
