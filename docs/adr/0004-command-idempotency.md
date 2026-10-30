# ADR-004: Persist original successful command responses

Date: 2026-10-30. Status: Accepted.

## Context

A client may lose a successful checkout response and retry after prices or order status change. Reconstructing the response from current rows is not the original outcome. Concurrent duplicates need database arbitration.

## Decision

CreateOrder keys are scoped to authenticated user, versioned operation, and key. Normalize the accepted item body, store canonical body plus SHA-256, and enforce the unique identity in PostgreSQL. Original order, reservations/history/event, and exact successful status/body bytes/content type/Location commit together. A replay compares canonical bodies and returns that original 201 snapshot; another valid normalized body returns 409. Recheck authentication/role on replay; do not accept a user ID from the client.

Equivalent line ordering, UUID casing, and duplicate grouping normalize identically. Current catalog prices/status are not part of the request body or replay validation. Successful entries are retained for v1 data lifetime. Failed transactions leave no cached result/claim. Handle concurrent unique conflicts by rolling back/using a valid subsequent transaction; do not query inside an aborted PostgreSQL transaction. Unknown commit outcome is resolved by the same operation identity.

StartPayment has a separate user/operation/order/key identity and persists the accepted attempt response; a failed attempt is not recreated under an old key. Stock adjustments have their own operation UUID/body comparison. Provider attempts, captures/events/refunds, outbox, inbox, and consumer effects each use independent identities defined in architecture.

## Alternatives and consequences

In-memory caches lose replay across processes/restarts. Returning current order state would violate response replay. Hash-only comparison could theoretically conflate bodies; storing canonical input makes intent inspectable. Exact response bytes and indefinite retention consume storage and couple snapshots to their API version. They must exclude volatile authentication/trace headers and secrets. No v1 automatic key expiry avoids reopening old operations accidentally.

## Verification

Exercise duplicate same/different normalized bodies, concurrent duplicates, user isolation, failed-claim rollback, dropped response, replay after price/status/deadline changes, and exact body/status/Location equality using actual PostgreSQL and HTTP. See [CreateOrder contract](../project-spec.md#5-createorder-idempotency-contract).
