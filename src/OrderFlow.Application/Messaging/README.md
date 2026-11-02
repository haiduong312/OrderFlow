# Durable messaging boundary

Milestone 03 adds PostgreSQL OutboxMessages and ProcessedMessages (inbox) with independent deduplication identities, versions, state/attempt/due/lease metadata and constraints. Both share OrderFlowDbContext with Identity/idempotency and future business changes. Inbox has no FK to the outbox, allowing external events. No transport, publisher, lease claimant, fake job loop, or in-memory durability substitute is registered. Dispatch, per-consumer progress and lease-token protocol remain milestone 14 work; orders only add their own events in 11/12.
