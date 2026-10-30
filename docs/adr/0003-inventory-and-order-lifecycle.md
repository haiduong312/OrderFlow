# ADR-003: Database-coordinated reservations and explicit order lifecycle

Date: 2026-10-30. Status: Accepted.

## Context

Multiple API/worker processes can race to reserve the last unit, confirm payment, cancel, or expire an order. Process-local locks cannot preserve stock across those processes. Payment processing can be delayed beyond the reservation deadline.

## Decision

Maintain `Available = OnHand - Reserved` with row CHECKs `0 <= Reserved <= OnHand`. Checkout reserves every line atomically; each order/product reservation is Active, Consumed, or Released. Confirmation subtracts quantity from both OnHand and Reserved; cancellation/expiry subtracts only Reserved. Terminal reservations never reactivate. Append uniquely referenced history in the same transaction as each effect.

Orders start PendingPayment and expire exactly 15 minutes after CreatedAt. Acquire the order lock, then sample injected UTC time; equality (`now >= ExpiresAt`) is expired. The locked eligibility decision is the linearization point. An on-time provider capture processed after expiry is still late for allocation; provider timestamps do not restore inventory. Reads do not mutate state; worker lag may conservatively retain expired stock until a command/expiry pass releases it.

Use Read Committed, consistent row-lock ordering, affected-row guards, explicit versions, and unique constraints. SQL owns durable coordination; do not share DbContext across threads. Cancellation/expiry/payment serialize through the order row. Any late capture is recorded and compensated by a durable full-refund intention, never order resurrection. Admin fulfillment has only Confirmed -> Processing -> Shipped -> Completed.

## Alternatives and consequences

In-process locks fail across instances; optimistic read-then-write alone can oversell. Serializable isolation everywhere would still need retry/idempotency and is unnecessary for the bounded row operations. Explicit locks reduce throughput and require deterministic order. Worker lag reduces availability conservatively. Host clock synchronization is required. Cross-table ledger/reservation sums are transactional invariants, not impossible cross-table CHECKs.

## Verification and reference

Test last-unit and multi-line rollback, all three time boundaries without sleeps, cancellation/payment/expiry races with separate PostgreSQL connections, history uniqueness, and refund scheduling. State tables are authoritative in [project specification](../project-spec.md). SQL behavior follows [PostgreSQL isolation guidance](https://www.postgresql.org/docs/18/transaction-iso.html).
