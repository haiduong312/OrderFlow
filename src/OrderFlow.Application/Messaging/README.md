# Durable messaging boundary

Milestone 03 adds PostgreSQL OutboxMessages and ProcessedMessages (inbox) with independent deduplication identities, versions, state/attempt/due/lease metadata and constraints. Both share OrderFlowDbContext with Identity/idempotency and business changes. Inbox has no FK to the outbox, allowing external events. No transport, publisher, lease claimant, fake job loop, or in-memory durability substitute is registered. Dispatch, per-consumer progress and lease-token protocol remain milestone 14 work; checkout 12 now writes one Pending ordering.order-created event atomically with its order/reservations/history and original response. No event is dispatched yet.

Milestone 13 additionally persists cancellation/expiry and forward fulfillment events with status history/release in the same unit of work. They are pending durable intentions; no dispatcher or external side effect is simulated.
