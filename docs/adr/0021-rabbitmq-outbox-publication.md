# ADR-021: RabbitMQ outbox handoff, fenced claims and finite retry topology

Date: 2026-11-12. Status: Accepted. Supersedes ADR-005's broker-free/local-delivery choice and architecture's requirement that the producer row wait for every consumer. Other payment/inbox guarantees remain unchanged.

## Context

The requested milestone adds RabbitMQ to existing committed outbox facts. A producer cannot atomically commit PostgreSQL and RabbitMQ. Confirm loss or a crash before the SQL acknowledgement may publish twice; a broker confirm cannot prove that a business consumer committed its effects. Checkout must not depend on broker availability.

## Decision

Only the separate, opt-in Worker references RabbitMQ.Client **7.2.2**. Development/test broker is **4.4.0-management**, pinned by image digest. Application defines IEventPublisher, the versioned contract and PostgreSQL orchestration; API producers keep their existing transaction and do not contact the broker.

Claim one due row with FOR UPDATE SKIP LOCKED in a short transaction; store owner, random token, 60-second lease and incremented count. Dispose scope before publishing. Each pass processes at most 20 rows and idles five seconds; exhausted-claim cleanup is separately bounded to 20 rows. Eight committed claims maximum, including crashes; classified transient failures use 5/15/30/60/120/300/600/600-second delays. Permanent configuration/envelope failures and exhaustion retain Failed evidence. Unknown exceptions stop visibly, leaving a reclaimable lease. Completion requires matching InFlight status, owner, token and unexpired lease in a single SQL UPDATE. A claim fences SQL acknowledgements, not already sent network effects.

Persist producer W3C trace context once. EnvelopeVersion=1 is independent of payload SchemaVersion. Include EventId, EventType, OccurredAt UTC, producer, aggregate identity/version, correlation/causation, optional trace and object-shaped data. EventId and body do not change on retry. SQL freezes envelope identity/content, prevents attempt regression and mutation of Published evidence; migration preserves existing rows with null optional trace.

Declare durable topic exchanges, durable quorum queues and bindings before publishing. Messages are persistent and mandatory; 7.2.2 CreateChannelOptions enables both confirms and confirmation tracking. Await BasicPublishAsync so tracked basic.return/NACK cannot be mistaken for positive routing confirmation. Only then mark Published. Confirm timeout/connection loss is uncertain and can republish the same identity. One protocol semaphore serializes all use of a connection's publisher channel; correctness across workers remains in PostgreSQL. Automatic client recovery is disabled: each classified failure discards the uncertain channel and later reconnects/redeclares. Connection/RPC/publish budgets are bounded; shutdown leaves work recoverable.

Finite topology: main work -> retry exchange -> TTL delay queue -> retry-ready exchange -> retry work -> dead exchange -> parked dead queue. No automatic cycle. Delivery limit five bounds repeated unacknowledged/requeued work at each work stage; explicit NACK(requeue:false) immediately advances it. At-least-once quorum dead lettering and reject-publish prevent silent source removal when the downstream target is unavailable. Queue length defaults to 10,000; overflow NACK is retryable and may already have routed elsewhere. This is one delayed retry stage, not a general consumer framework. Quorum length limits can overshoot slightly.

The current integration queue is a real durable downstream destination; no business handler drains it yet. Future independent notification/reporting consumers need separate queues/bindings and their own transactional inbox/effects, then manual ACK. They must tolerate duplicates and out-of-order versions. Sharing one queue between independent effects would load-balance instead of fan out.

## Consequences and recovery

Published now means broker handoff only. There is no end-to-end exactly-once claim. Retained Failed/DLQ evidence requires operator review; automatic unsafe reset/replacement identities are not added. The [runbook](../messaging.md) describes reconnect, lease recovery, DLQ handling and forward topology changes. Hardcoded versioned topology arguments favor a reproducible portfolio setup; incompatible argument changes require a planned new version rather than deleting queues. One-node development quorum persistence does not provide production HA. TLS, clustered failover, runtime roles, business consumers and audited replay remain later milestones.

## Version-sensitive references and evidence

- [Official .NET guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide): channel ownership/concurrency and payload lifetime.
- [7.2.2 channel options](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.2/projects/RabbitMQ.Client/CreateChannelOptions.cs), [tracked publishing](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.2/projects/RabbitMQ.Client/Impl/Channel.BasicPublish.cs) and [PublishException](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.2/projects/RabbitMQ.Client/Exceptions/PublishException.cs): awaited confirms and returns.
- [Quorum queues](https://www.rabbitmq.com/docs/quorum-queues) and [dead lettering](https://www.rabbitmq.com/docs/dlx): durability, bounded delivery and at-least-once forwarding requirements.

Real PostgreSQL/RabbitMQ tests exercise unavailable/returned/NACK/confirmed publication, exact-body duplicate after dropped real ACK, lost local acknowledgement, concurrent claims, stale owner, bounded exhaustion, cancellation/restart, persisted queue restart, finite DLX and populated upgrade. See [implementation evidence](../implementation-status.md).
