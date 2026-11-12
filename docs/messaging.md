# RabbitMQ outbox publication

Checkout, stock and payment transactions save OutboxMessages atomically with business changes. They never call RabbitMQ. An opt-in Worker delivers committed rows; broker downtime delays publication without undoing an order. Published means **mandatory routing and broker confirmation**, not that any business consumer ran. No notification/reporting handler is installed in this milestone.

## Local setup

Use the SDK pinned in global.json, Docker Engine and Compose v2+. RabbitMQ **4.4.0-management** and RabbitMQ.Client **7.2.2** are pinned; see [ADR-021](adr/0021-rabbitmq-outbox-publication.md). Prepare private files once, without overwriting existing credentials:

```bash
mkdir -p .local
for name in postgres rabbitmq; do
  if [ ! -e ".local/${name}-password" ]; then
    (umask 077; openssl rand -hex 32 > ".local/${name}-password")
  fi
done
export ORDERFLOW_DB_PASSWORD_FILE="$PWD/.local/postgres-password"
export ORDERFLOW_RABBIT_PASSWORD_FILE="$PWD/.local/rabbitmq-password"
docker compose --profile dev config --quiet
docker compose --profile dev up -d --wait --wait-timeout 90
```

For the broker alone use `docker compose --profile messaging up -d --wait rabbitmq`; Compose still requires the DB password-file variable during configuration validation. Ports bind loopback only: 5672 AMQP and 15672 management; override ORDERFLOW_RABBIT_PORT / ORDERFLOW_RABBIT_MANAGEMENT_PORT if needed. Broker user is orderflow_dev, vhost orderflow. No secret is stored in tracked configuration; the wrapper reads the Compose secret then calls the official entrypoint. Do not enable shell tracing or print environment/credentials. Health diagnostics run as rabbitmq, avoiding a root-owned Erlang cookie during first startup.

The Docker daemon must be able to read bind-mounted secret/script paths. An isolated/remote daemon needs its own secure file injection; config validation alone does not prove those mounts work. The stable hostname/nodename and named volume preserve broker identity across container recreation. `docker compose ... down` without `--volumes` retains data; do not delete a volume/queue to fix a connection problem. Changing the bootstrap password file does not change an already initialized broker user's password.

Follow [persistence setup](persistence.md) to privately set ConnectionStrings__OrderFlow and explicitly migrate:

```bash
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
export DOTNET_ENVIRONMENT=Development
export OutboxPublishing__Enabled=true
export RabbitMq__HostName=127.0.0.1
export RabbitMq__Port="${ORDERFLOW_RABBIT_PORT:-5672}"
export RabbitMq__VirtualHost=orderflow
export RabbitMq__UserName=orderflow_dev
export RabbitMq__Password="$(cat "$ORDERFLOW_RABBIT_PASSWORD_FILE")"
dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile
```

Apply migration **20261112030213_AddOutboxPublishing** before enabling publication. No host auto-migrates. Normal API needs no RabbitMq settings/package; seed commands suppress background loops. Expiry keeps its existing default; payment/refund loops keep their separate opt-in flags.

Optional Worker settings: RabbitMq:Prefix=orderflow, PublishTimeoutSeconds=15 (1–30), RetryDelayMilliseconds=5000 (100–300000), QueueMaxLength=10000 (1–1000000). Existing queues must match arguments; change a prefix/version through a reviewed migration/drain plan, not by deleting evidence. Connection/RPC timeout is five seconds, heartbeat 30 seconds, auto-recovery disabled; outbox reconnects/redeclares after classified failures. This dev connection has no TLS; production transport/permissions/HA are deferred.

## Envelope and publication flow

```json
{
  "envelopeVersion": 1,
  "eventId": "00000000-0000-0000-0000-000000000001",
  "eventType": "ordering.order-created",
  "schemaVersion": 1,
  "occurredAt": "2026-11-12T03:00:00+00:00",
  "producer": "Ordering",
  "aggregateType": "Order",
  "aggregateId": "00000000-0000-0000-0000-000000000002",
  "aggregateVersion": 1,
  "correlationId": "00000000-0000-0000-0000-000000000003",
  "causationId": "00000000-0000-0000-0000-000000000004",
  "traceParent": null,
  "traceState": null,
  "data": { "total": "25.00", "currency": "USD" }
}
```

This is a contract example, not a complete order projection fixture. Data is the original JSON object, preserving invariant money strings. SchemaVersion versions data; EnvelopeVersion versions the wrapper. Producer W3C trace context is captured once, optional for legacy rows, and also sent as AMQP trace headers. MessageId=EventId, Type/routing key=EventType, CorrelationId matches the body. Persistent application/json UTF-8 properties and version headers accompany it. No payload, credentials or trace baggage are logged.

1. A fresh scope claims one due Pending or expired InFlight row with FOR UPDATE SKIP LOCKED, increments AttemptCount and stores owner/token/60-second expiry. Cleanup of exhausted leases is limited to 20 rows. Commit and dispose the scope.
2. Outside SQL transactions, serialize the stable body, acquire the publisher channel gate, declare durable topology if connecting, and await persistent mandatory publication with tracked confirms. Return/NACK/timeout cannot produce local Published.
3. A fresh scope conditionally updates only the matching live claim. Success becomes Published. Classified transient failures return Pending with 5/15/30/60/120/300/600/600-second backoff. Permanent errors or eight claims become retained Failed. A crash/shutdown leaves the lease reclaimable; unknown exceptions stop visibly.
4. Up to 20 sequential rows per pass, then five-second cancellable polling. Multiple processes coordinate via PostgreSQL; the in-process semaphore only protects channel frames. No global order is promised.

When an ACK or the final SQL acknowledgement is lost, the next worker can send the same event again. A lease cannot retract network I/O. Every future business consumer must atomically commit `(ConsumerName,EventId)` inbox/effects before manual ACK and reject unknown versions visibly. Independent consumers need independent queues/bindings.

## Durable finite retry/dead-letter policy

All exchanges are durable topic exchanges; queues are durable, nonexclusive, non-auto-delete quorum queues. Every binding below uses `#` and preserves the original routing key:

| Exchange | Queue | Forwarding |
| --- | --- | --- |
| orderflow.events.v1 | orderflow.integration.v1 | NACK(requeue:false), delivery exhaustion -> retry exchange |
| orderflow.retry.v1 | orderflow.integration.delay.v1 | TTL 5000 ms -> retry-ready exchange |
| orderflow.retry-ready.v1 | orderflow.integration.retry.v1 | NACK(requeue:false), delivery exhaustion -> dead exchange |
| orderflow.dead.v1 | orderflow.integration.dead.v1 | Park for review; no automatic forwarding |

The main queue/binding is declared before any successful publish. Main and retry-work consumers are future work. There is **one delayed retry stage**; x-delivery-limit=5 also bounds poison redelivery when a future consumer crashes or requeues. Do not write infinite immediate NACK(requeue:true) loops. Delay/dead queues have no application business consumer. Dead-letter sources use at-least-once quorum forwarding with reject-publish; downstream unavailability/full queues retain source work until forwarding is confirmed. Bounded queue length defaults to 10,000 (quorum may overshoot slightly); overflow can NACK the publisher. Messages are persistent. One development node survives tested restart; it provides no multi-node availability guarantee.

## Observe and recover

Read durable publisher evidence through a configured DB connection:

```sql
SELECT "EventId", "EventType", "Status", "AttemptCount", "NextAttemptAt",
       "LeaseOwner", "LeaseExpiresAt", "PublishedAt", "LastErrorCode", "LastFailureAt"
FROM messaging."OutboxMessages"
ORDER BY "OccurredAt", "EventId" LIMIT 100;
```

Inspect broker topology/queue depth without removing messages:

```bash
docker compose --profile messaging exec --user rabbitmq rabbitmq rabbitmqctl list_queues -p orderflow name type durable messages_ready messages_unacknowledged arguments
docker compose --profile messaging exec --user rabbitmq rabbitmq rabbitmqctl list_bindings -p orderflow
```

- **Broker down:** restore endpoint/connectivity and start broker. Due Pending rows retry automatically; InFlight rows wait for lease expiry. Exercise by stopping only RabbitMQ, creating an order through the existing authenticated checkout flow, observing its committed Pending event, then starting broker. If eight claims have exhausted, use review below.
- **Unroutable:** LastErrorCode=broker_unroutable; verify exchange/vhost/queue/binding. The next reconnect redeclares known topology. Do not mark Published manually.
- **NACK/capacity:** inspect queue depths/disk alarms; resolve downstream backlog/capacity. NACK can be uncertain if another bound queue accepted the message. Retrying preserves EventId.
- **Confirm timeout / lost acknowledgement:** duplicates are expected. Inspect original EventId and consumer inbox; never create a replacement event to hide uncertainty. A stale owner cannot complete after token/expiry changed.
- **Configuration mismatch/auth failure:** broker_topology_or_permission_error / broker_authentication_failed is retained Failed immediately. Fix credentials/permissions or plan a new topology version. Never delete queues to satisfy a redeclaration.
- **Failed after bounded attempts:** retain row/error/count and investigate. Audited same-identity operator replay is milestone 28 and not implemented here. Do not reset counters, edit immutable payload or fabricate a new EventId. A reviewed recovery procedure/tool must reuse the original identity and account for possible prior publication.
- **Dead queue:** no automatic redrive. Inspect original envelope/x-death and diagnose consumer/schema before any recovery. A future audited tool must preserve EventId, avoid duplicate effects through the consumer inbox, require routing/confirm before acknowledging the parked copy, and record who replayed it. Until then, leave parked evidence in place.

Structured Worker events 1801/1802/1803 report safe failure code/attempt/uncertainty, lost lease and database contention. Broker-level confirmation is never advertised as successful notification/reporting. Queue/message inspection through management UI can consume messages; use read-only operations during diagnosis.

## Verification and limits

See [testing](testing.md) for the focused command and [status](implementation-status.md) for executed results. Tests include real broker unavailability, mandatory return, actual full-queue NACK, positive confirmation, dropped ACK followed by duplicate delivery, local acknowledgement rollback, concurrent claims, stale owner, bounded exhaustion, cancellation recovery, uncommitted-row isolation, immutable facts, populated migration and persistent broker restart. No production cluster failover/network partition/OS kill, end-to-end business consumption or universal exactly-once guarantee is claimed.
