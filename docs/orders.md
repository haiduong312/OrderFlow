# Customer orders, cancellation and Admin fulfillment — milestone 13

This slice completes Customer list/detail/cancellation and pulls the requested Admin fulfillment endpoints from 19 into 13. The [milestone 14 expiry worker](expiry-worker.md) now finalizes overdue orders automatically; payment confirmation/consumption, refunds, UI and outbox dispatch remain future work. No public/manual status-setting or confirmation endpoint is provided. See [ADR-016](adr/0016-order-management-transitions.md), [checkout](checkout.md), [authorization](authorization.md) and [implementation status](implementation-status.md).

## Endpoints

All paths use the existing /api prefix and one-origin Identity cookies. Every browser POST requires a valid X-CSRF-TOKEN paired with that browser's cookie. Unknown JSON fields are rejected, including client actor/owner/status fields on cancellation.

| Endpoint | Role / request | Result |
| --- | --- | --- |
| GET /api/orders | Customer; page/pageSize/status | Own orders only; owner filter precedes count and paging |
| GET /api/orders/{id} | Owning Customer | Current snapshots/version/status/history/expiry metadata; other/missing is 404 |
| POST /api/orders/{id}/cancel | Owning Customer + CSRF; empty body or {} | 200 Cancelled/current Cancelled; overdue PendingPayment commits Expired then returns 409 order_expired |
| GET /api/admin/orders | Admin; page/pageSize/status/customerId | All matching orders, with bounded paging |
| GET /api/admin/orders/{id} | Admin | Current order/items/status history, missing stays 404 |
| POST /api/admin/orders/{id}/transitions | Admin + CSRF; targetStatus/expectedVersion/optional reason | Exactly one permitted forward fulfillment transition, 200 current detail |

Admin is not implicitly Customer. Dual-role users must still own an order on Customer paths. No client userId/header/query may override the Customer identity predicate. Customer endpoints accept no customerId filter; an extra query parameter cannot change the server's ownership scope. Anonymous returns 401, wrong endpoint role 403, absent/non-owned Customer resource 404, with ProblemDetails and no HTML redirects. Responses are no-store.

Lists default to page=1, pageSize=20; size is 1–100. PageRequest rejects invalid or overflowing offsets. Order is CreatedAt DESC then Id DESC, so equal timestamps have a unique tie breaker. Status uses exact case-sensitive enum names and filters **persisted** status. An overdue row may therefore appear in status=PendingPayment with effectiveStatus=Expired. Only the Admin query accepts a nonempty customerId UUID filter. Pages/counts are evaluated in the same read-only Repeatable Read snapshot; separate page requests can still shift when new orders arrive or status filters change. Existing owner/time and new all-order/time and status/time B-tree indexes support these query shapes.

Summary rows include id/customerId/status/version/createdAt/expiresAt/USD total/currency and expiry metadata; detail adds immutable item snapshots and status history. No fake empty payment/refund summaries are returned for unimplemented features. Monetary values remain invariant two-place strings, integer quantities/versions remain JSON integers.

## Effective expiry without read-side mutations

Read responses include:

```json
{
  "status": "PendingPayment",
  "version": 1,
  "expiresAt": "2035-01-01T00:15:00.000000Z",
  "expiry": {
    "observedAt": "2035-01-01T00:15:00.000000Z",
    "effectiveStatus": "Expired",
    "isExpired": true,
    "canCancel": false
  }
}
```

A PendingPayment order is overdue at equality: now>=ExpiresAt. Its stored status/version, reservations and audit/event rows stay unchanged on GET. Stored Expired is always effectively Expired. Paid/fulfillment/Cancelled states do not become Expired merely because their old payment deadline passed. Admin views set canCancel=false; an owning Customer view canCancel only for unexpired PendingPayment. The frontend can use server observedAt and the fixed deadline to show a countdown/effective expiry, but these fields are advisory; mutation eligibility is rechecked under a database lock.

Count/page and detail/items/history use short Repeatable Read READ ONLY transactions, normal SELECTs and bounded statement budgets. They do not lock rows FOR UPDATE, scan-and-expire a batch or call another service. The view is a coherent committed snapshot, not a promise that no mutation can occur immediately afterward. While automatic expiry is disabled or delayed, overdue stock remains conservatively reserved until a safe write finalizes expiry.

Checkout response bytes remain unchanged and independent of these newer read DTOs. Replaying CreateOrder after cancellation still returns its original PendingPayment/201 snapshot; GET returns the current Cancelled or Expired order.

## Central transition rules

| Current state | Operation | Next/effect |
| --- | --- | --- |
| PendingPayment, now<ExpiresAt | Owner cancel | Cancelled; release each Active reservation |
| PendingPayment, now>=ExpiresAt | Owner cancel | Expired; release each Active reservation; commit then 409 order_expired |
| Cancelled | Owner cancel again | 200 current representation; no new version/history/movement/event |
| Expired | Owner cancel again | 409 order_expired; no new effects |
| Confirmed/Processing/Shipped/Completed | Customer cancel | 409 state_conflict; no release/refund |
| Confirmed | Admin target Processing | Forward one step with expectedVersion |
| Processing | Admin target Shipped | Forward one step with expectedVersion |
| Shipped | Admin target Completed | Forward one step with expectedVersion |
| Anything else | Admin transition | 409 state_conflict; stale version is 409 version_conflict |

An unknown/numeric/case-variant status, missing/nonpositive expectedVersion, reason over 500 characters or unknown JSON field is 400 validation_failed. An existing enum name such as Confirmed/Cancelled is syntactically valid but cannot be used as an Admin transition target from an unsupported state: 409. Admin cannot confirm a PendingPayment order or cancel an order. There is no paid-order cancellation/refund route or UI.

OrderTransitions defines the graph and cancellation decision; Order methods apply it, advance version and produce the status-history fact. The timestamp uses injected UTC TimeProvider truncated to microseconds. OrderStatusHistory is append-only with order/version key, FromStatus/new Status, actor kind/UUID, OccurredAt and reason. Customer reasons are fixed server descriptions; Admin reason is trimmed, with a fixed default if omitted/blank. No actor/user role is taken from the body. Cancellation triggered by expiry is recorded as the Customer command that discovered it, not an invented System user.

## Cancellation transaction walkthrough

1. Authenticate/authorize/validate CSRF and the empty-body contract. Begin one scoped Read Committed transaction, set LOCAL lock_timeout=5s and statement_timeout=20s; provider command timeout remains 30s.
2. SELECT the Order FOR UPDATE using **both** id and authenticated owner id. Another user cannot lock/release it. Refresh any unchanged tracked order from the locked row; pending edits are rejected. All competing lifecycle writers use this first order lock.
3. Sample UTC business now only after obtaining that lock. The locked decision is the linearization point, as the existing contract specifies. A valid cancellation may commit after the deadline if inventory locks delay it; it does not resample an unrelated later clock and change its chosen outcome.
4. Central rules decide Cancelled, Expired, duplicate Cancelled or conflict. A duplicate needs no inventory lock/effect. A paid/expired conflict cannot release stock. For a real pending transition, obtain immutable order items, acquire balances FOR UPDATE in canonical product-ID order, then the matching reservations FOR UPDATE in that order. Never acquire catalog/order locks after inventory locks.
5. Validate every reservation is Active and agrees with the order/item quantity and immutable times. Inconsistent persisted facts fail visibly and roll back, rather than report fake success or release a guessed quantity.
6. Mark each reservation Released with ReleasedAt and Cancelled/Expired release reason; decrement Reserved only, preserve OnHand, increment balance revision and append a linked Customer Release StockMovement. ReleaseStock:v1 is keyed by reservation UUID, separate from ReserveStock:v1. Existing unique operation/revision keys and stock CHECKs protect effects. Persist each release within the SAME transaction; later failures roll back real SQL already written.
7. Append OrderStatusHistory and one Pending ordering.order-cancelled/order-expired outbox row at the new version. Save, build detail from this transaction and commit. Only **after successful commit**, the expired case returns 409 order_expired. Retrying cannot create another release/history/event. The API never claims an expired result while rolling back its release unintentionally.

There is no network call inside the business transaction or process-local correctness mutex. Known PostgreSQL contention states reuse the explicit sanitized 503 classification and structured warning; unknown failures remain logged/sanitized 500. No automatic business retry is added. After an uncertain cancellation result, retrying the same cancel or reading current detail reconciles state. For Admin after an uncertain result, refetch current state/version before deciding the next step; do not automatically invent a new expectedVersion.

## Admin fulfillment transaction

Example request:

```json
{"targetStatus":"Processing","expectedVersion":2,"reason":"Ready for fulfillment"}
```

The Admin controller calls the lifecycle service. It takes the same Order FOR UPDATE, reads fresh state/version, samples UTC, compares expectedVersion and applies only the central forward edge. It appends the Admin actor/status-history fact plus ordering.order-processing/order-shipped/order-completed outbox intention, saves and commits atomically. It never reads/changes stock or reservations. Concurrent same-version requests yield one successful step; the loser sees the fresh version and receives 409. Repeating the same target at the new version is also invalid, not a second successful step. The old payment deadline does not block fulfillment of a Confirmed order.

There is currently no production payment-confirmation writer. Integration tests seed Confirmed status/history in owned test databases to exercise these endpoints, explicitly without claiming capture/consume correctness. Do not add an unauthenticated test/confirmation endpoint or expand payment scope to manufacture this prerequisite. Full payment integration remains later work.

## Migration and manual verification

New migration: **20261105084550_AddOrderManagement**. It renames existing ordering.OrderHistory to OrderStatusHistory, preserving rows/constraints/index/append-only triggers; adds FromStatus/ActorKind and reservation ReleasedAt/ReleaseReason; expands ledger release checks; adds list indexes and terminal-reservation metadata protection. A supported legacy creation-history row retains its exact actor UUID/time/reason/version and gains Customer actor kind. The temporary backfill default is removed afterward.

The generated DROP/CREATE history operation was replaced before any database execution. The migration is forward-only: Down throws rather than discard release/actor audit metadata. The table rename requires a coordinated schema/application update; old binaries still querying OrderHistory are incompatible with the renamed table. No developer/production database was migrated by the implementation work.

From the repository root, with private ConnectionStrings__OrderFlow supplied for the operator migration:

```bash
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
# Docker running: tests create/migrate/drop only their owned databases.
dotnet test OrderFlow.slnx -c Release --no-build --no-restore
```

Use matching ArtifactsPath for alternate outputs. Hosts never migrate automatically. For HTTP verification, follow the existing [authentication](authentication.md) cookie/CSRF runbook and [checkout](checkout.md) to create two Customers, an Admin, stocked products and pending orders. Do not print or commit passwords/cookies/tokens.

1. As Customer A, GET /api/orders?page=1&pageSize=2, then page=2; check count/order and own IDs. Query status=PendingPayment/Cancelled and compare own totals. Customer B must see only their rows and get 404 for A's detail/cancel.
2. GET A's order detail and observe the fixed deadline/expiry metadata/history. Repeated GET/list calls must not change version, reservations, stock, history or outbox.
3. POST A's /cancel with {} and a fresh CSRF token. Check Cancelled/version+1, one new history/event and Reserved release without changing OnHand. Repeat and check no further effects. Confirm other pending orders/reservations and unrelated stock remain intact. Missing CSRF is 400.
4. Use fake time in automated tests for before/equal/after deadline and lock-wait cases; no manual sleep is needed. Overdue cancellation returns 409 order_expired after committed Expired/release; GET then exposes stored Expired.
5. As Admin, list all or filter customerId/status, read details/history; Customer/anonymous access to these paths must be denied. A PendingPayment transition to Processing or Confirmed is 409. Valid forward fulfillment needs an already Confirmed order from the future verified payment writer; current integration fixtures cover that state, with no new production seed/bypass command.

## Tests and trade-offs

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrderLifecycleTests|FullyQualifiedName~OrderManagement'
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

Actual PostgreSQL tests use real cookies/CSRF, per-case migrated DBs and separate contexts. Same-order cancellation/Admin races rendezvous immediately BEFORE the first Order FOR UPDATE; an inventory barrier behind that lock would deadlock falsely. Tests count both distinct contexts and observe only the winning cancellation reaching inventory. Two independently authenticated hosts cancel the same order with exactly one durable release per reservation. Distinct-order shared-stock tests check canonical inventory order and reconciliation. A separate held order lock plus one-way pre-statement arrival signal tests expiry decision after lock acquisition without arbitrary sleeps. Outbox fault injection verifies earlier SQL releases and status changes roll back. Upgrade/SQL tests preserve legacy history and reject terminal reactivation or history mutation.

Trade-offs: offset pagination may shift between separate requests; read-only snapshot transactions add a small overhead and no-store responses require refetch. Per-product release saves add roundtrips but provide explicit rollback evidence. Host clocks must be synchronized. Automatic expiry now ships in the separate Worker; dispatch, payment confirmation/consumption/refunds, UI and process-crash/uncertain-commit fault suites remain pending. Late-payment compensation remains the existing specified future workflow, without extra paid-order cancellation/refund UX.

## Expiry worker follow-up (14 expiry slice)

System expiry now shares OrderReservationRelease with cancellation. Only Active reservations are decremented; already Released facts are preserved, while Consumed/missing/mismatched facts on a pending order fail visibly. System history actors have null ActorUserId; human actors remain required. See the [expiry runbook](expiry-worker.md) and ADR-017 for migration, lock ordering and deterministic cancellation/worker overlap tests. The original milestone-13 transaction/migration evidence above remains historical.
