# OrderFlow v1 — implementation contract

Baseline: 2026-10-30. Milestone 01 is documentation only. Statements below describe required future behavior, not implemented features. See [implementation status](implementation-status.md), [architecture](architecture.md), and [ADRs](adr/README.md).

## 1. Purpose and scope

OrderFlow is a portfolio application for explaining practical ASP.NET Core, SQL concurrency, and full-stack development at middle level. Build a modular monolith: one ASP.NET Core API, one separately runnable .NET worker, one PostgreSQL database, and a React/TypeScript client. Modules are Identity, Catalog, Inventory, Ordering, Payments, Notifications, and Reporting. Group modules by feature folders in a shared application project; do not create a project for every module or abstraction.

The browser accesses the client and `/api` through one reverse-proxy origin. Use ASP.NET Core Identity cookies, TanStack Query for server state, and Zustand for cart/UI state. A durable simulated gateway is the only v1 payment provider. See ADR-001, ADR-002, and ADR-005.

No source, tests, package manifests, migrations, previous ADRs, or milestone roadmap existed at inspection. The `.git` directory was empty and `git status --short --branch` failed because this was not a valid Git repository. Consequently, the milestone names and dependencies in the status document establish the initial 01–29 roadmap; they are not a recovered external plan. No Git initialization is part of milestone 01.

If later instructions contradict this contract, report the conflict before changing dependent behavior. Record material changes in a superseding ADR and update acceptance criteria, roadmap, and tests together. Routine implementation choices do not require another approval.

## 2. Actors, ownership, and product boundaries

| Actor | Permissions |
| --- | --- |
| Anonymous | Read active catalog; register as Customer; log in; obtain antiforgery token; health checks |
| Customer | Manage own session; create orders; list/read own orders and payment/refund summaries; request payment; cancel own eligible orders |
| Admin | Manage catalog and stock; inspect all orders; advance fulfillment; read reporting and operational failures |
| Worker | Expire reservations, submit/reconcile payments and refunds, dispatch durable events, update notifications/reporting through application services |
| Simulator callback | Signed provider authentication only; record a provider event; never use a browser cookie as authority |

Registration never accepts roles. Provision the initial Admin through an explicit operator bootstrap using external secrets; no default password or public role-assignment endpoint. Authorization checks run on the server and constrain database queries. Customer access to another user's order, attempt, or refund returns 404; missing authentication returns 401; missing endpoint role returns 403. Admin access uses explicit `/admin` endpoints and does not bypass Customer endpoint ownership checks. An Admin is not implicitly a Customer.

Catalog products have immutable normalized SKU, name, description, USD price, active flag, and an optimistic version. Normalize SKU with trim and uppercase invariant ASCII; accept 1–64 characters `[A-Z0-9-]`. Names are trimmed, nonempty, at most 200 characters; descriptions at most 2,000. Products are deactivated, not deleted. Historical order lines snapshot product ID, SKU, name, unit price, and quantity. Later price/name/deactivation changes do not alter existing orders or prevent payment for an already reserved order.

Fulfillment is a demonstration of status changes only. v1 does not collect shipping addresses, charge shipping fees, or call a carrier. Cart data is an estimate and is not an inventory reservation. No guest checkout.

## 3. Money, quantities, identifiers, and time

- Money uses .NET `decimal` and PostgreSQL `numeric(18,2)`. Currency is exactly `USD`. Prices and order totals are positive. Deltas/refunds have explicitly defined signs; refund amounts are positive.
- Public JSON money is an invariant string with exactly two fractional digits, such as `"12.50"`; no JSON numbers, exponent, thousands separators, or implicit rounding. Reject input with more than two fractional digits. Compute `lineTotal = unitPrice * quantity` and `total = sum(lineTotal)` on the backend; reject overflow above `9999999999999999.99` rather than truncating it.
- Client calculations parse money strings into integer cents using `bigint`; do not pass through floating-point `Number` or serialize `bigint` directly. Format/serialize money back to decimal strings. Counts and quantities remain JSON integers.
- Checkout accepts 1–100 distinct products, quantities 1–1,000 per product after normalization. Duplicate product lines are combined with checked addition, then sorted by canonical product UUID. Reject fractional/negative/zero quantities, excessive merged quantities, invalid IDs, and unknown fields. Products must be active and have sufficient availability at checkout.
- Stock counts are 32-bit nonnegative integers; adjustment deltas are nonzero integers. Reject overflow, underflow, or any adjustment reducing OnHand below Reserved. Opening stock starts at zero and is introduced through an audited adjustment.
- IDs are opaque UUIDs. Public enums use the exact case-sensitive strings in this document. JSON property names use camelCase.
- Business timestamps are UTC `DateTimeOffset`, stored as PostgreSQL `timestamptz`, exposed as ISO 8601 strings with `Z`. Persist and compare at microsecond precision (truncate sub-microsecond ticks). Use injected `TimeProvider` for time and test clocks; never client time or real sleeps to decide expiry.
- Set `CreatedAt = now` and `ExpiresAt = CreatedAt + 15 minutes` once when creating the order after acquiring checkout locks. Do not extend expiry on replay, new payment attempts, worker retries, or delayed callbacks.
- For existing-order mutations, acquire the order lock first, then sample `now` for the eligibility decision. `now < ExpiresAt` is valid; `now >= ExpiresAt` is expired. The locked decision is the linearization point; a transaction may commit later. Provider-reported capture time is audit data and cannot extend the deadline. Hosts must synchronize UTC clocks; skew is an operational limitation, not solved by an in-process lock.

## 4. Explicit state transitions

Any transition absent from these tables is rejected or treated as a verified duplicate with no new effect. Persist state changes and their history in the same transaction.

### Orders

| From | To | Actor/trigger | Guard and atomic effects |
| --- | --- | --- | --- |
| None | PendingPayment | Customer CreateOrder | Reserve every line; set 15-minute deadline; persist response snapshot and event |
| PendingPayment | Cancelled | Owning Customer | `now < ExpiresAt`; release all Active reservations; emit event |
| PendingPayment | Expired | Worker or payment/cancel command | `now >= ExpiresAt`; release all Active reservations; emit event |
| PendingPayment | Confirmed | Verified successful capture | `now < ExpiresAt`, exact amount/currency/reference, all reservations Active; consume inventory, link winning capture, emit event |
| Confirmed | Processing | Admin | Expected version and current state match |
| Processing | Shipped | Admin | Expected version and current state match |
| Shipped | Completed | Admin | Expected version and current state match |

Cancelled, Expired, and Completed are terminal. There is no Failed order status: a failed attempt leaves an unexpired order PendingPayment so another attempt may be started. A late capture never changes a terminal order or moves fulfillment backward. Retrying cancellation of an already Cancelled own order returns its current representation without another release. Cancelling an overdue PendingPayment order expires it and returns 409 `order_expired` after committing the expiry. Cancellation of Expired or a paid order returns 409. Repeating an Admin transition returns 409; the client refetches current state.

Reads do not mutate expired orders. During worker lag, a GET can show PendingPayment with an elapsed ExpiresAt; the UI uses the deadline for display, and commands always enforce expiry. Stock may remain conservatively reserved until a write/worker releases it.

### Reservations and inventory

| From | To | Condition | Inventory delta for quantity q |
| --- | --- | --- | --- |
| None | Active | Successful checkout | Reserved `+q`; OnHand unchanged |
| Active | Consumed | Winning payment confirmation | Reserved `-q`; OnHand `-q` |
| Active | Released | Cancellation or expiry | Reserved `-q`; OnHand unchanged |

Consumed and Released are terminal. Exactly one reservation exists per `(orderId, productId)`. Repeated consumption/release has no effect. Release reason is Cancelled or Expired. `Available = OnHand - Reserved`, `OnHand >= 0`, `Reserved >= 0`, `Reserved <= OnHand` always hold. Every stock adjustment, reservation creation, consumption, and release appends history with operation reference, product/reservation/order references when applicable, deltas, UTC timestamp, and actor/system reason. Never update or delete history to hide an error; correct stock with another audited adjustment.

### Payment attempts, captures, and refunds

| Entity/from | To | Meaning |
| --- | --- | --- |
| Attempt: None | Queued | Durable payment intention accepted; no provider call yet |
| Attempt: Queued | Pending / Unknown / Failed / Captured | Submit/query result: accepted, uncertain, definitively rejected, or verified capture |
| Attempt: Pending | Unknown / Failed / Captured | Reconciliation or signed callback |
| Attempt: Unknown | Pending / Failed / Captured | Query provider using the original attempt key; never create a replacement merely on timeout |
| Attempt: Failed | Captured | A later verified capture overrides a failure observation; apply confirmation/refund rules, never discard captured funds |
| Attempt: Captured | Captured | Monotonic capture evidence; delayed failure cannot overwrite it |
| Refund: None | Pending | Persist full-refund intention for an ineligible/surplus capture |
| Refund: Pending | Unknown / Failed / Succeeded | Submit/query result; timeout is Unknown, permanent rejection is Failed |
| Refund: Unknown | Pending / Failed / Succeeded | Reconcile by the same refund key |
| Refund: Failed | Pending / Succeeded | Explicit operator retry with same identity, or verified later success |
| Refund: Succeeded | Succeeded | Duplicate observation; no second refund |

Lease ownership/attempt counters are separate delivery metadata, not payment facts. Only one Queued/Pending/Unknown attempt per order may exist. Creating a payment while one exists returns that attempt (202); after definitive failure, a new attempt is allowed before expiry. An already paid/terminal order rejects new attempts with 409. For a request arriving after expiry, commit expiry and return 409. All attempts use the order's authoritative immutable total, never browser amounts.

Record verified captured USD funds even when too late. If the order is expired/cancelled, the amount is wrong, or another capture already won, persist the capture and exactly one full-refund intention for that provider capture. If still overdue PendingPayment, expire/release it in the same transaction. For an amount mismatch before expiry, leave the order PendingPayment and do not consume stock. Distinct surplus captures are each refunded separately; a duplicate callback for one capture does not create a new refund. No partial payment, split payment, partial refund, or customer-requested refund in v1. Refund failure never resurrects an order or returns its stock a second time.

Validate provider event identity, signature, attempt/order association, currency, and amount against stored facts. A signed event with an unknown/mismatched reference or unsupported currency is quarantined for reconciliation and recorded as an operational failure; retain its authenticated observation without attaching it to an arbitrary order or confirming it. Only USD captures/refunds are supported by the v1 simulator and financial workflow; non-USD observations require operator investigation rather than inventing a conversion/refund path. A known USD capture with an unexpected amount is still recorded and refunded in full. Only an exactly matched USD capture may confirm an order. Fault cases are covered by tests.

## 5. CreateOrder idempotency contract

`POST /api/orders` requires `Idempotency-Key`: 1–128 case-sensitive visible ASCII characters, no surrounding whitespace. The namespace is `(authenticatedUserId, operation="CreateOrder:v1", key)`; never accept user ID in the body. Keys and successful snapshots are retained for the lifetime of v1 data, including expired/cancelled orders. No automatic cleanup or reuse window.

The only body is `{"items":[{"productId":"<uuid>","quantity":2}]}`. Normalize as section 3 describes, serialize a versioned canonical representation, and store both canonical body and SHA-256 hash. Compare canonical bodies as well as hashes. Property order, line order, UUID casing, and equivalent duplicate-line grouping do not change meaning. Product price, role, totals, payment status, address, and other undeclared fields are invalid input. The same key with a different valid normalized body returns 409 `idempotency_conflict`.

Within one PostgreSQL transaction, claim the unique idempotency identity, validate/lock catalog and stock, reserve all lines, create the order and event, and persist the original successful response. Persist status 201, content type, Location, and exact UTF-8 JSON body bytes (not a reserialized `jsonb` object). The snapshot includes ID, PendingPayment status, CreatedAt, ExpiresAt, currency, authoritative totals, and line snapshots. Replays return that original response even if the order has since changed; GET returns current state. Do not persist volatile cookie/trace headers in the snapshot.

Concurrent requests contend on the database unique identity; the loser reads the committed snapshot and compares bodies. Do not use a check-then-insert without a unique constraint. Handle conflicts without continuing an aborted PostgreSQL transaction. If the winner rolls back, a subsequent contender may claim the key. Business/validation failures roll back the claim and are not cached; changing a failed request body with that key is allowed. Bounded contention timeout returns retryable 503 without claiming success. A lost connection around commit is uncertain: retry the same key and reconcile from the stored result.

Other deduplication identities are independent: payment client request, provider attempt key, provider capture ID, provider event ID, refund provider key, outbox event ID, inbox consumer/event ID, notification delivery ID, and stock operation ID. Reusing CreateOrder's key for all effects is forbidden. See ADR-004 and ADR-005.

## 6. REST endpoint inventory

All paths below are under `/api`. Browser POST/PUT/PATCH/DELETE endpoints, including anonymous registration/login and logout, require valid ASP.NET Core antiforgery cookie/token pair; use `X-CSRF-TOKEN`. Refresh the request token after login/logout. The signed callback is the sole business antiforgery exemption. Responses are JSON except stated otherwise. Roles and ownership still apply on every replay. Limit browser JSON request bodies to 128 KiB and callback bodies to 64 KiB.

| Method/path | Access | Request / successful response |
| --- | --- | --- |
| GET `/auth/antiforgery` | Anonymous | No-store request token + antiforgery cookie; no auth secret in body |
| POST `/auth/register` | Anonymous + CSRF | Email/password; create Customer; 201; no auto-login |
| POST `/auth/login` | Anonymous + CSRF | Identity password login; 200 user summary + HttpOnly cookie; generic invalid-login error |
| POST `/auth/logout` | Authenticated + CSRF | 204; clear session |
| GET `/auth/me` | Authenticated | 200 user ID and roles; no password/security stamp |
| GET `/products` | Anonymous | Active products, bounded pagination, name/SKU search |
| GET `/products/{id}` | Anonymous | Active product and advisory available count; inactive/absent 404 |
| GET `/admin/products` | Admin | Active/inactive catalog, version and stock summary |
| GET `/admin/products/{id}` | Admin | Current product, version and stock |
| POST `/admin/products` | Admin + CSRF | SKU/name/description/price; 201, active product with zero stock |
| PUT `/admin/products/{id}` | Admin + CSRF | Name/description/price/active + expectedVersion; 200; SKU immutable |
| POST `/admin/products/{id}/stock-adjustments` | Admin + CSRF | operationId UUID, delta, reason; 201 adjustment; same ID/body replays, changed body 409 |
| GET `/admin/products/{id}/stock-history` | Admin | Paginated append-only stock/reservation history |
| POST `/orders` | Customer + CSRF | Items and Idempotency-Key; 201 original order snapshot + Location |
| GET `/orders` | Customer | Paginated own orders; optional state filter |
| GET `/orders/{id}` | Owning Customer | Current order, lines, payment/refund summary and version |
| POST `/orders/{id}/cancel` | Owning Customer + CSRF | Empty body; 200 current Cancelled order; expiry/state conflict 409 |
| POST `/orders/{id}/payment-attempts` | Owning Customer + CSRF | Empty body + Idempotency-Key; 202 persisted attempt + Location |
| GET `/orders/{id}/payment-attempts/{attemptId}` | Owning Customer | Current attempt, capture/refund summaries; no provider secrets |
| GET `/admin/orders` | Admin | Paginated all orders, state/customer filters |
| GET `/admin/orders/{id}` | Admin | Order, history, payment/refund summary |
| POST `/admin/orders/{id}/transitions` | Admin + CSRF | targetStatus + expectedVersion; 200 current order |
| POST `/payments/simulator/callback` | Signed provider only, CSRF exempt | Authenticated event; 202 after durable inbox receipt, also for exact duplicate |
| GET `/admin/reports/sales` | Admin | UTC `[from,to)` range, daily USD sales, current order-state counts, projection checkpoint |
| GET `/admin/operations/failures` | Admin | Bounded list of failed/quarantined jobs and sanitized error codes |
| GET `/health/live` | Anonymous | Process liveness only; no sensitive detail |
| GET `/health/ready` | Anonymous | Database/schema readiness; worker readiness also exposed by worker's internal health mechanism |

Payment request idempotency uses `(userId, "StartPayment:v1", orderId, key)` and stores the original accepted attempt representation; replay must not create another attempt, including after a definitive failure. A fresh key may create a new attempt only if eligible. Two distinct keys racing while one attempt is active link to that same attempt through the database uniqueness rule. Stock adjustment operation identity is scoped to the Inventory operation, with actor, target, delta, and reason compared on replay.

Collections use `page` (default 1), `pageSize` (default 20, maximum 100), stable timestamp/ID ordering, and `{items,page,pageSize,totalCount}`. Invalid filters/pagination return 400. Reporting requires `from < to`, maximum 366 days; days are UTC. Version conflicts return 409 `version_conflict`; product updates and fulfillment increments use explicit integer versions. A stale version cannot overwrite a concurrent change.

Errors use Problem Details with stable `code`, `traceId`, and validation fields where relevant: 400 validation/antiforgery, 401 unauthenticated, 403 forbidden, 404 absent/not owned, 409 state/idempotency/stock/version conflict, 413 excessive body, 429 throttled, 503 transient infrastructure failure, 500 unexpected failure. Never expose stack traces, SQL, or secrets. Cookie authentication returns API 401/403, never redirects to HTML login. Callback invalid signature returns 401, malformed schema 400, oversized body 413; same provider event ID with different authenticated payload returns 409 and an alert. Quarantine valid unknown references durably, acknowledge 202, and expose the failure for reconciliation.

## 7. Consistency and durable messaging

Checkout, stock changes, cancellation, expiry, and accepted-capture confirmation are strongly consistent inside PostgreSQL transactions. They include state/history and an outbox event. No payment, refund, HTTP, email, or reporting network call occurs while a business transaction is open. Database-only module collaboration within the same unit of work is allowed.

Outbox dispatch, provider submission/reconciliation, callback processing after durable receipt, refunds, notifications, and reporting are eventual. `202` is durable acceptance, not successful payment/refund. A temporarily stopped worker delays expiry/release, capture processing, refunds, notifications, and projections; it cannot allow an overdue confirmation. Reporting/notification failure cannot roll back checkout. GET order is authoritative for order state; reporting includes checkpoint/updatedAt and can lag.

Every integration event has an immutable envelope:

```json
{
  "eventId": "<uuid>",
  "eventType": "ordering.order-confirmed",
  "schemaVersion": 1,
  "occurredAt": "2026-10-30T07:00:00.000000Z",
  "producer": "Ordering",
  "aggregateType": "Order",
  "aggregateId": "<uuid>",
  "aggregateVersion": 2,
  "correlationId": "<uuid>",
  "causationId": "<command-or-event-uuid>",
  "data": {}
}
```

Envelope examples are placeholders, not executable fixtures. Order state events carry a full minimal projection snapshot: order ID, status, version, creation/update times, USD total, immutable confirmedAt and winning capture reference when applicable; exclude passwords, tokens, cookies, addresses, and email. Persist confirmedAt at the winning confirmation; later fulfillment snapshots retain it so reordered events cannot shift sales into a different day. Event types initially cover order-created/cancelled/expired/confirmed/processing/shipped/completed, inventory-adjusted, payment-captured, and refund-requested/succeeded/failed, prefixed by their producing module as in the example. Increment the order aggregate version once per state change. Event IDs are independent of aggregate IDs and transport delivery attempts. Breaking payload changes require a new schema version; unknown versions are quarantined, never silently ignored.

Delivery is at least once. Outbox event identity is unique; each consumer's inbox `(consumerName,eventId)` is unique. Apply the consumer's durable effects and mark its inbox handled in one transaction. Lease expiry can run the same delivery again. Claim/complete using lease tokens; an old owner cannot acknowledge a newer lease. Global event ordering is not guaranteed. Reporting applies only snapshots newer than its stored aggregate version, and updates contributions/counters transactionally; older events cannot regress status or double-count sales. Order count uses latest projected state; sales recognize the one winning capture at Confirmed and its confirmation UTC day, not raw late/surplus captures or provider attempt counts. Compensation of unallocated captures is not negative sales. Notifications use a unique `(eventId,template,recipientUserId)` delivery identity; v1 uses a durable local delivery record, with no external email requirement.

Failed deliveries retain error code, count, next attempt, and last failure time. Transient retries are bounded; exhausted/permanent failures require operator attention. The runbook must retry the original operation/event identity, never manufacture a replacement capture/refund. No promise of exactly-once external side effects. Full database structure, lease protocol, and failure classifications are in architecture and ADR-005.

## 8. Acceptance criteria and required test evidence

These are implementation obligations. Milestone 01 passes only the documentation criteria D01–D06; A01–A24 remain unimplemented until evidenced in status.

| ID | Invariant / acceptance criterion | Required evidence and milestone |
| --- | --- | --- |
| D01 | Existing instructions/code/packages/migrations/Git inspected and unrelated work preserved | Inspection record, 01 |
| D02 | Scope, roles, endpoints, money/time, ownership, transitions and edge cases explicitly fixed | Review this specification, 01 |
| D03 | Module/schema ownership, transaction and eventual boundaries, envelope and deduplication identities documented | Architecture + ADRs, 01 |
| D04 | 01–29 dependency map and pending acceptance/test obligations recorded | Status roadmap, 01 |
| D05 | Initial accepted ADRs exist and local document links resolve | ADR index + link/consistency check, 01 |
| D06 | No business source, package installation, migration, commit, or deployment added | File inventory, 01 |
| A01 | Reproducible stable dependencies; API/client/worker separately start; one browser origin | Restore/build, lockfiles and smoke checks, 02–04, 25 |
| A02 | Identity cookies, CSRF, roles, ownership and no public role escalation | Actual HTTP tests incl. anonymous login CSRF, cross-user 404, cookie flags, API 401/403, 05–06, 19 |
| A03 | Positive exact USD money, bounded integer quantities, immutable line snapshots and overflow rejection | Domain + JSON/SQL/client tests, 07, 11, 20–21 |
| A04 | `0 <= Reserved <= OnHand`, Available formula, guarded stock adjustments, version conflicts | Direct SQL constraint tests + concurrent HTTP adjustments, 08–09 |
| A05 | Reserve every line or none; no overselling across independent contexts/processes | PostgreSQL rollback and last-unit races; all participants reach stock update, 12 |
| A06 | CreateOrder same normalized body replays exact original successful response; different body conflicts; keys user/operation scoped | HTTP/SQL tests including concurrent duplicates, lost response, later status/price changes and failed claim rollback, 10, 12 |
| A07 | Reservation Active -> Consumed/Released only; each inventory/history effect applied once | Domain + SQL/history tests, 08, 11–14, 18 |
| A08 | Deadline is exactly 15 minutes; equality expires; no replay/attempt extension | Fake-TimeProvider tests before/equal/after deadline and after lock waits, 11–14, 18 |
| A09 | Only owner cancels eligible PendingPayment; cancellation/expiry release Reserved without changing OnHand | HTTP + SQL tests, duplicate cancels, worker overlap, 13–14 |
| A10 | Winning capture atomically confirms and decrements OnHand/Reserved; invalid/mismatched capture cannot confirm | Signed callback + SQL transaction/failure-injection tests, 15–18 |
| A11 | Late/surplus capture recorded, never resurrects/consumes released stock; one refund intention per capture | Expiry/cancel/payment races, duplicate and out-of-order events, refund failure tests, 18 |
| A12 | Intention committed before provider I/O; uncertain result reconciles by original key | Simulator instrumentation + crash after provider success/before local completion, 15–18, 26 |
| A13 | One active attempt/order; distinct identities for attempts, captures, callbacks and refunds | Unique constraint + concurrent request/callback tests, 15–18 |
| A14 | Signed callbacks verify raw body, timestamp and event identity; cookie/unsigned requests rejected | HTTP tests incl. tampering, stale delivery signature, duplicate ID/different payload and unknown references, 17 |
| A15 | Fulfillment only Confirmed -> Processing -> Shipped -> Completed; no paid cancellation | Domain/HTTP tests incl. skipped/reverse/stale transitions and role denial, 19 |
| A16 | State/history/outbox atomically commit or roll back; history append only with operation refs | SQL rollback, uniqueness, runtime-role mutation rejection and ledger reconciliation tests, 08–19, 25 |
| A17 | Worker scopes do not share DbContext; duplicate/expired lease cannot duplicate durable effects | Two-worker barrier tests + crash/lease-token recovery, 14–18, 23–24, 26 |
| A18 | Outbox/inbox independently deduplicate; newer projection cannot regress on older event | Duplicate/out-of-order + transactional inbox/projection tests, 14, 23–24 |
| A19 | Only classified transient failures retried with bounded attempts; permanent failures observable | Fault injection/exhaustion/operator retry tests, 14–18, 26–28 |
| A20 | Frontend trusts server authority, exact cents, recoverable checkout retries, CSRF refresh | Client unit/component tests and browser flows, 20–22, 27 |
| A21 | Local notifications uniquely recorded; reporting lags safely and counts winning sales once | Consumer SQL tests, rollback/reorder tests and reporting auth/range tests, 23–24 |
| A22 | Structured redacted logs, readiness and safe configuration, no optional external checkout dependency | Log/config/readiness inspection + worker-down scenario, 25–28 |
| A23 | No network calls inside business transactions, no EF InMemory evidence for SQL invariants | Gateway transaction instrumentation + PostgreSQL-backed integration suite, 12, 15–18, 26 |
| A24 | Contract, operating guide, demo and limitations agree with verified implementation | Final acceptance walkthrough and reproducible run commands, 29 |

Race tests must use separate connections/DbContexts and deterministic gates/barriers at the contested operation; assert every participant arrived and completed/failed as expected. Do not block all participants behind a barrier while one already holds the lock they need to reach it. Verify both result and durable rows/history/outbox/refunds. No arbitrary sleeps as concurrency proof. Boundary-time tests use fake time, not elapsed wall-clock time. Integration tests use actual PostgreSQL, never EF InMemory or SQLite substitutes. Unit-test meaningful domain logic; avoid tests that merely repeat framework plumbing.

## 9. Deferred features

Outside v1: real payment providers, card handling, homemade JWT/refresh tokens, microservices, message broker, Redis/distributed cache, event sourcing, generic CQRS/MediatR scaffolding, paid-order cancellation, returns, customer refunds, discounts, tax, shipping fees/providers/addresses, multiple sales currencies, guest checkout, split/partial payments, partial refunds, stock locations/backorders, product images/uploads, email/SMS provider integration, self-service password reset/email verification, social login/MFA, public Admin registration, analytics beyond the stated report, automated data retention/deletion, and deployment to a public environment. Do not implement these as prerequisites unless a later milestone explicitly changes scope.
