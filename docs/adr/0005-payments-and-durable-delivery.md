# ADR-005: Simulated payments, compensation, and at-least-once delivery

Date: 2026-10-30. Status: Accepted.

## Context

A provider can capture funds while the merchant loses the response, retries, expires an order, or crashes. A queue lease can expire while its previous owner is still delivering. Neither a database transaction nor a lease guarantees exactly-once external effects.

## Decision

v1 uses a durable simulator adapter, not a real provider or frontend success flag. Store merchant attempt/refund intentions first; commit; call/query the simulator outside the transaction; persist observations in a fresh scope. The simulator's independent transactions and provider-key uniqueness model uncertain outcomes and repeated calls. One unresolved active attempt per order is enforced by a partial unique index. Never replace an Unknown attempt just because of a timeout.

Authenticate callback raw bytes with dedicated HMAC credentials/timestamp, then durably record provider event and inbox before 202. Callback processing locks the order, records unique capture evidence, and atomically either confirms/consumes or records a full-refund intention. Late, cancelled, mismatched, and surplus captured funds cannot resurrect orders. Capture evidence is monotonic; delayed failure cannot erase it. Each distinct capture has its own compensation identity. Unknown references are quarantined for reconciliation, not guessed.

Use transactional outbox/inbox in PostgreSQL. Event IDs, provider event IDs, provider attempt keys, capture IDs, refund keys, and consumer/effect IDs are independent namespaces. Claim queues with short leases and fencing tokens; perform external I/O after commit. Inbox handling/effects commit together. Notifications and reporting are eventual and independently deduplicated. Versioned full snapshots protect projections against reordering.

Only classified transient failures receive bounded retries; uncertainty is a reconciliation state. Exhausted/permanent failures remain visible, with audited operator replay using the original identity. External delivery remains at least once even with idempotency. Real provider integration and guarantees are outside v1.

## Alternatives and consequences

Calling a gateway during checkout would hold SQL locks across slow/unreliable I/O. Treating timeout as failure could double-charge. Trusting leases alone would duplicate effects. A broker would add infrastructure without removing these requirements. PostgreSQL polling is sufficient at portfolio scale but introduces lag, retained failed work, and operator responsibilities. Sharing a database with the simulator is a local testing convenience and must not collapse the two transaction boundaries.

## Verification

Prove restart recovery, timeout after capture, expired leases with two workers, callback duplicates/tampering/reordering, surplus/late captures, one compensation per capture, refund uncertainty and dead-letter visibility. Instrument gateway calls to assert no merchant transaction is open. Delivery defaults and signature protocol are in [architecture](../architecture.md); public behavior is in [project specification](../project-spec.md).
