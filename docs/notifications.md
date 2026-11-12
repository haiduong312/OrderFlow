# Durable user notifications

The RabbitMQ worker creates a local notification for the order owner from each supported order event. It commits Notification + consumer inbox + NotificationCreated outbox together, then ACKs the delivery. No email/realtime network call runs in that transaction. Broker confirms remain separate from this consumer's database completion.

## Enable locally

Complete [PostgreSQL setup](persistence.md) and [RabbitMQ setup](messaging.md) first, using private DB/broker credentials. Existing RabbitMQ.Client 7.2.2 / broker 4.4.0 pins are unchanged. Explicitly migrate before enabling the worker:

```bash
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
export DOTNET_ENVIRONMENT=Development
export Notifications__Enabled=true
export OutboxPublishing__Enabled=true
export Notifications__ConsumersPerQueue=1
export Notifications__Prefetch=4
dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile
```

Set ConnectionStrings__OrderFlow and RabbitMq__Password privately as in the linked runbooks; never put secrets in CLI arguments/tracked settings. Migration **20261112035709_AddNotifications** creates the notification schema/table, indexes, restrictive FKs and identity/read/inbox guards. No automatic startup migration or historical backfill. API startup registers read services but no RabbitMQ client/consumer. Seed commands suppress background jobs.

Consumer and publisher flags are independent: use a separate process with only Notifications enabled if desired. The publisher declares notification queues/bindings before sending future events even if consumption is off. Events Published before this binding existed are not retroactively copied; old integration backlog requires a reviewed replay/backfill plan. Do not discard that backlog or automatically reprocess all prior events.

## Event contract and recipient

Consumer identity: `notifications.orders.v1`. The dedup key is `(ConsumerName,MessageId)` in ProcessedMessages; notifications additionally enforce `(SourceEventId,Type,RecipientUserId)`. Independent consumers can handle the same EventId with their own identity. Do not include a worker/process instance in ConsumerName.

Supported EnvelopeVersion=1 / SchemaVersion=1 order types:

| Event type | Historical order status |
| --- | --- |
| ordering.order-created | PendingPayment |
| ordering.order-confirmed | Confirmed |
| ordering.order-cancelled | Cancelled |
| ordering.order-expired | Expired |
| ordering.order-processing | Processing |
| ordering.order-shipped | Shipped |
| ordering.order-completed | Completed |

Exactly one intended recipient is the stored order owner; there is no Admin broadcast. Required data: orderId, userId, version and status; orderId/version must equal aggregate identity/version. Stored owner and immutable OrderStatusHistory authorize correlation. The consumer never trusts a supplied userId alone and never changes order/stock/payment state. An old event is valid if its version/status is in history, even after a later cancellation/fulfillment. Notification CreatedAt is injected local processing UTC time; ReadAt is optional and becomes immutable on first read. The list is a history of notices, not a replacement for GET order's current status.

MessageId must be the envelope EventId in UUID D format; Type must match EventType; content type application/json. Body is 1–65536 bytes, valid UTF-8/object JSON, maximum depth 32 with no duplicate object properties. Producer=Ordering, AggregateType=Order, nonempty identities and valid optional W3C context are required. Additional v1 fields are ignored. The exact original body's SHA-256 is retained: identical redelivery is a no-op, different bytes under a handled EventId are poison. Never reserialize/alter an original envelope during recovery.

Schema 0/missing/older and higher unknown schema/envelope versions are poison, not silently interpreted as v1. Unknown event types have no notification binding; an unexpected type explicitly sent to a notification queue is rejected. Supporting a new version requires explicit parser/migration/test changes, then a reviewed same-identity DLQ replay.

NotificationCreated uses the existing versioned envelope with EventType=`notifications.notification-created`, SchemaVersion=1, Producer=Notifications, AggregateType=Notification, AggregateId=notification ID, AggregateVersion=1, original CorrelationId and CausationId=source EventId. Data contains notificationId, recipientUserId, sourceEventId, type, orderId and createdAt. Producer trace context is preserved. The event is intended for future authorized realtime delivery; no socket/email consumer ships now. Its routing key does not bind this notification consumer, preventing recursion.

## Bounded subscriptions and failure path

Default prefix orderflow gives these independent durable quorum destinations (same bounded queue length/reject-publish/at-least-once DLX rules as the foundation topology):

| Source exchange | Queue | Next destination |
| --- | --- | --- |
| orderflow.events.v1, seven exact routing keys | orderflow.notifications.orders.v1 | notifications.retry exchange |
| orderflow.notifications.retry.v1 | orderflow.notifications.orders.delay.v1 | TTL -> notifications.retry-ready exchange |
| orderflow.notifications.retry-ready.v1 | orderflow.notifications.orders.retry.v1 | notifications.dead exchange |
| orderflow.notifications.dead.v1 | orderflow.notifications.orders.dead.v1 | Park; no automatic forwarding |

Consumer subscribes to main and retry-work only. NACK(requeue:false) advances a failed delivery through one delayed retry stage (default 5 seconds), then parks it. Poison JSON/schema/identity/owner failures and classified transient PostgreSQL failures follow this finite path; each failed DB transaction rolls back effects/inbox/outbox. Quorum delivery-limit five also bounds repeated consumer-crash/requeue redelivery at each work stage. No immediate requeue loop. Unknown exceptions fault the subscription and stop the worker visibly, leaving unacknowledged work for operator recovery.

ConsumersPerQueue=1–4, default 1; Prefetch=1–32, default 4. There are two work queues, so default concurrency is two transactions and at most eight unacknowledged messages. Each subscription owns a connection/channel and dispatches sequentially; each delivery owns a scoped DbContext. ACK/NACK uses multiple:false. Do not concurrently use the same DbContext or channel to raise throughput. Shutdown cancellation rolls back unfinished work and closes subscriptions; committed-but-unacknowledged work may redeliver safely.

Connection/session attempts are bounded to eight per subscription slot per worker run (1/5/15/30/60/120/300-second reconnect backoff). Startup budget is 30 seconds with existing five-second connection/RPC timeouts. Authentication/permission/topology mismatch fails immediately. After exhaustion or unexpected application failure, fix the cause and restart the worker; no fake healthy fallback. Counters are connection/session attempts, independent of the broker message retry path.

## Authenticated API

All endpoints use the existing Identity cookie. They accept no owner/recipient parameter; Admin also sees only their own notifications. Details/non-owned/missing read return 404. Responses are no-store.

| Method/path | Behavior |
| --- | --- |
| GET /api/notifications?page=1&pageSize=20&unreadOnly=false | Own collection `{items,page,pageSize,totalCount}`, page size <=100, CreatedAt DESC then Id DESC |
| GET /api/notifications/unread-count | Own `{count}` for ReadAt=null |
| GET /api/notifications/{id} | Own notification details |
| POST /api/notifications/{id}/read | Empty body or `{}`, valid CSRF; first read stores UTC ReadAt (at least CreatedAt if the clock moved backwards), repeats preserve it |

DTO: id, sourceEventId, type, orderId, createdAt, readAt. Source identity is immutable; current recipient is determined by authentication. Read endpoint needs no Idempotency-Key because its conditional update is naturally idempotent. Concurrent reads preserve the first timestamp and cannot mark another user's row.

Manual verification:

1. Start API with normal cookie/CSRF setup and Worker as above; sign in as Customer and create/cancel an order through existing APIs. Wait for asynchronous publication/consumption, then GET notifications/unread-count.
2. Reuse the cookie jar and obtain a fresh request token from GET /api/auth/antiforgery. POST one own notification's /read with `X-CSRF-TOKEN` and `{}`. Repeat and compare unchanged ReadAt; unread count drops once.
3. With a second user's real cookie/token, GET that ID and POST /read: both 404. The second user's list/count contain only their own notices; missing CSRF on an owner's POST returns 400.
4. Run focused tests below for redelivery/crash/poison rather than changing production payloads/counters. Tests synchronize at actual SQL INSERT/UPDATE or broker delivery callbacks, without arbitrary race sleeps.

## Observe/recover

```sql
SELECT "Id","RecipientUserId","SourceEventId","Type","OrderId","CreatedAt","ReadAt"
FROM notifications."Notifications" ORDER BY "CreatedAt" DESC,"Id" DESC LIMIT 100;
SELECT "ConsumerName","MessageId","Status","ProcessedAt" FROM messaging."ProcessedMessages"
WHERE "ConsumerName"='notifications.orders.v1' ORDER BY "ReceivedAt" DESC LIMIT 100;
SELECT "EventId","CausationId","Status","LastErrorCode" FROM messaging."OutboxMessages"
WHERE "EventType"='notifications.notification-created' ORDER BY "OccurredAt" DESC LIMIT 100;
```

Read-only broker inspection:

```bash
docker compose --profile messaging exec --user rabbitmq rabbitmq rabbitmqctl list_queues -p orderflow name messages_ready messages_unacknowledged arguments
```

Structured log 1901 records poison code with safely parsed EventId, 1902 classified database retry, 1903 reconnect attempt/queue, 1904 ACK after committed inbox. No raw message/user text/credentials are logged. The DLQ retains original body/properties/x-death; no handled inbox row is committed for failed processing. Keep DLQ data while diagnosing, deploy explicit support/fix first, then use a reviewed same-EventId/raw-body redrive. A future audited tool must confirm republishing before ACKing the parked copy. No automated redrive/backfill/clearing inboxes is provided. Do not reset source identity, ReadAt, inbox or outbox facts. Production monitoring/TLS/roles/cluster HA/archival remain future work.

## Automated checks

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Notification|FullyQualifiedName~OutboxPublishingTests'
```

Uses actual PostgreSQL/RabbitMQ and real cookie/CSRF. See [status](implementation-status.md) for exact commands/results/limits. Crash test injects failure after observing committed SQL facts and before sending ACK, then closes the real channel and redelivers. It is not an OS-process-kill claim or exactly-once external-delivery claim.
