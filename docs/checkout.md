# Customer checkout — milestone 12

Implemented: authenticated Customer checkout, immutable order/item snapshots, Active stock reservations, creation history, exact successful idempotency replay and an OrderCreated outbox intention. Owner-constrained detail reads provide current status. This request pulls the needed parts of 09–11/13 into 12; it does not implement consume/release/cancellation, automatic expiry, payment, listing, fulfillment or outbox dispatch. See [ADR-015](adr/0015-idempotent-checkout.md), [specification](project-spec.md), [testing harness](testing.md) and [status](implementation-status.md).

## HTTP contract

Browser writes require the existing HTTPS Identity cookie plus X-CSRF-TOKEN. Customer role is required; Admin-only accounts are not implicit Customers. All paths retain the repository's /api prefix.

```http
POST /api/orders
Content-Type: application/json
Idempotency-Key: checkout-unique-client-intention
X-CSRF-TOKEN: <fresh request token paired with this browser cookie>

{"items":[{"productId":"11111111-1111-1111-1111-111111111111","quantity":2}]}
```

The UUID is illustrative, not seeded. Accept 1–100 **distinct** nonempty product IDs and integer quantities 1–1000. Duplicate IDs are rejected with 400, including differently cased representations of the same UUID. This supersedes the original duplicate-merging rule. Fractional/string/null/out-of-range quantities, missing fields, null items and unknown fields are rejected. No price, total, currency, owner, role, status, time or paid flag is accepted from the client, including nested item prices. Request body limit is 128 KiB.

Supply exactly one Idempotency-Key header: 1–128 case-sensitive visible ASCII characters, without spaces. Keys are scoped to authenticated user + CreateOrder:v1. Reordering distinct items/properties or changing UUID casing is equivalent; changing quantity/product under a successful key returns 409 idempotency_conflict. A failed transaction caches nothing; a corrected request may reuse its key. A successful key/response is retained for the lifetime of v1 data. Use a fresh key for a genuinely new checkout, preserve the old key/body after timeout or response loss.

201 returns Location `/api/orders/{id}`, `Cache-Control: no-store` and the stored application/json response bytes. Example shape:

```json
{
  "id": "22222222-2222-2222-2222-222222222222",
  "status": "PendingPayment",
  "version": 1,
  "createdAt": "2026-11-05T08:00:00.000000Z",
  "expiresAt": "2026-11-05T08:15:00.000000Z",
  "total": "25.00",
  "items": [{
    "productId": "11111111-1111-1111-1111-111111111111",
    "sku": "DEMO-SKU",
    "name": "Product snapshot",
    "unitPrice": "12.50",
    "quantity": 2,
    "lineTotal": "25.00"
  }],
  "currency": "USD"
}
```

All prices/totals come from locked database products and exact decimal calculations; line and aggregate values exceeding 9999999999999999.99 are rejected. Monetary JSON values are invariant two-place strings, quantity/version remain integers. Item order is canonical UUID order. CreatedAt is injected UTC time truncated to microseconds **after acquiring all checkout locks**. ExpiresAt is fixed at CreatedAt+15 minutes.

GET `/api/orders/{id}` reads current status/version and immutable snapshots, constrained in SQL by both ID and authenticated owner. Other customer's/missing order returns the same 404; anonymous returns 401 and wrong endpoint role 403. Reads do not update status or release reservations. PendingPayment/Active rows can remain after ExpiresAt while the future expiry worker is absent; availability remains conservatively reduced. Future writes must use `now >= ExpiresAt` as expired under the order lock. No deadline extension occurs on replay or read.

A valid successful replay returns its original 201/body/content type/Location even after catalog edits/archival, elapsed deadline or later order status changes. GET is the current-state source. Replays still require valid authentication, Customer policy, CSRF, valid request shape and key. They do not re-check current catalog/stock. Cookies and volatile trace headers are never stored in the idempotency snapshot.

| Condition | Result |
| --- | --- |
| Invalid shape/key, duplicate IDs, numeric overflow | 400 validation_failed |
| Missing/invalid CSRF | 400 antiforgery_failed |
| Anonymous / wrong role | 401 unauthenticated / 403 forbidden |
| Missing product, missing/not-owned order | 404 not_found |
| Product/category inactive or association changed outside the locked category set | 409 state_conflict; failed claim rolls back |
| A line lacks available stock | 409 insufficient_stock; entire order rolls back |
| Successful key used with different normalized body/hash | 409 idempotency_conflict |
| Stock revision exhausted | 409 stock_limit_exceeded |
| Classified lock/deadlock/serialization/server statement timeout | 503 service_unavailable; retry same key/body |
| Unknown database/application error | Sanitized/logged 500; no fake success |

## Transaction walkthrough

The controller only extracts the single key, calls CheckoutService and writes stored bytes. Global browser filters and the existing Customer policy remain in force. One scoped DbContext belongs to one request; no concurrent operations share it.

1. Validate key/items and normalize them before claiming anything. Canonical UTF-8 JSON has fixed version/property order, lower-case canonical UUID strings sorted in PostgreSQL UUID order and integer quantities. Compute SHA-256; also retain/compare the canonical text. Product prices/activity are deliberately absent from request identity.
2. Look for an already completed claim by user/operation/key. Return its original bytes if canonical text and hash match; otherwise 409. This fast replay path takes no business locks and makes no new order/reservation/event.
3. Begin a Read Committed transaction and set LOCAL lock_timeout=5s and statement_timeout=20s. Insert InProgress into messaging.IdempotencyRecords and SaveChanges **before** business locks. The unique index UX_Idempotency_User_Operation_Key arbitrates concurrent contenders. This claim is never committed independently.
4. If that INSERT raises its named 23505 unique violation, explicitly roll back/dispose the transaction and detach the losing Added claim. Only then read the committed winner in a valid connection state and compare/replay it. No query is made in an aborted transaction. PostgreSQL may instead let a waiting INSERT succeed if the prior contender rolls back.
5. Discover product/category associations. Lock all discovered categories FOR SHARE in canonical UUID order, then all requested products FOR SHARE in canonical product-ID order. SHARE is compatible with other checkouts and blocks catalog writers. Revalidate product activity, category activity and membership in the already locked category set. If association changed to a new unlocked category, roll back with state_conflict; never acquire a lower lock level late.
6. Acquire each inventory balance FOR UPDATE in stable product-ID order using InventoryLocking. It reads fresh values and refreshes any unchanged tracked snapshots. These locks serialize reservations and stock changes across hosts, not just inside one process.
7. Sample injected UTC now after the locks. Build the PendingPayment Order/version 1 and immutable OrderItems from database SKU/name/unit price, multiplying/summing checked domain amounts. Set one fixed 15-minute deadline. Persist Order/Items and its initial OrderHistory row. Orders.CheckoutRequestId has a unique restrictive FK to the claim.
8. For each sorted item, create one Active InventoryReservation, validate Available>=quantity and increment Reserved/revision without changing OnHand. Append a Customer/Reservation StockMovement linked to order/reservation/product; ReserveStock:v1 uses the reservation UUID as its independent operation identity. Save that item's effects within the same transaction. A failure on the second line really rolls back SQL already written for the first line and the order.
9. Add one Pending outbox row, event type ordering.order-created/schema version 1, producer Ordering, aggregate Order/version 1. EventId is independently generated; causation is the claim UUID and correlation is the new order UUID. Payload includes order/user IDs, status/version, USD total and times, excluding credentials and the raw idempotency key. No dispatcher/provider/other network call runs here.
10. Serialize the actual public order DTO to UTF-8 exactly once, complete the claim with 201/content type/Location/body bytes, save and commit. Only after commit deliver those same bytes. Order, items, reservations, balances, both audit stores, event and response snapshot commit together or not at all.

No implicit retry is enabled. PostgreSQL states 55P03/40P01/40001/57014 map to a structured warning with only SqlState and a sanitized 503, including the known InvalidOperationException wrapper from Npgsql's non-retrying execution strategy. Request cancellation remains cancellation; unknown exceptions are not classified as retryable. The provider command timeout is 30 seconds; test harness connection/server budgets remain additionally active. Connection loss around COMMIT is uncertain: resend the same key/body and reconcile the durable response rather than assume rollback. The code makes no external exactly-once claim.

## Schema and migration

Apply additive **20261105070955_AddCheckout** using the repository's pinned EF tool after building:

```bash
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
# Privately export ConnectionStrings__OrderFlow first; never print/commit the credential.
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
```

Use matching ArtifactsPath when choosing an alternate build directory. API/worker still never migrate or seed automatically. Existing catalog/stock/Identity/reliability data is preserved. Do not run Down against data you need: it removes order/reservation tables, and reservation audit rows cannot be converted back to the old movement contract safely.

| Table/change | Enforced contract |
| --- | --- |
| ordering.Orders | Owner/claim FKs, one order per checkout claim, positive numeric(18,2) USD total, finite exact 15-minute deadline, valid status/version, owner/time and pending-deadline indexes |
| ordering.OrderItems | Composite order/product PK, restrictive Order/Product FKs, quantity 1–1000, positive/range-bounded numeric prices/line totals with arithmetic CHECK, snapshot SKU/name bounds |
| ordering.OrderHistory | Order/version PK, owner actor FK, finite time/reason/state/version, append-only creation fact |
| inventory.InventoryReservations | One per order/product, order-item FK, balance FK, quantity/state/deadline checks; ID/order/product alternate key supports movement linkage |
| inventory.StockMovements | Nullable order/reservation references for old rows, composite reservation/order/product FK, Reservation/Customer kind branch and unique reservation operation identity; existing append-only/quantity/revision safeguards retained |

EF after-save guards and PostgreSQL triggers reject rewriting item/history rows and immutable order/reservation identities, quantities, money and deadlines, including raw SQL updates and item/history DELETE/TRUNCATE. Status/version columns are reserved for later lifecycle commands; defining state labels does not implement those commands. No cross-table CHECK is claimed for sum(item totals), Reserved=sum(active reservations) or ledger reconciliation: the transaction/use case enforces them, tests verify them. Runtime least-privilege roles remain deployment work.

## Manual HTTP verification

Use the existing [authentication runbook](authentication.md) to create an opt-in Development Admin, then create category/product/stock via [Catalog](catalog.md) and [Inventory](inventory.md). Register/login a separate Customer and refresh its CSRF token. Keep separate cookie jars and never paste passwords, cookies or tokens into Git/logs.

Using the runbook's BASE and Customer cookie jar, set PRODUCT_ID to an actual stocked product UUID. Write a non-secret request body and choose a new intention key:

```bash
printf '{"items":[{"productId":"%s","quantity":1}]}' "$PRODUCT_ID" > /tmp/orderflow-checkout-request.json
CHECKOUT_KEY="checkout-$(openssl rand -hex 16)"
# CUSTOMER_CSRF is privately obtained from /api/auth/antiforgery using the same cookie jar.
curl --fail-with-body --silent --show-error -b /tmp/orderflow-customer.cookies \
  -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $CUSTOMER_CSRF" \
  -H "Idempotency-Key: $CHECKOUT_KEY" --data-binary @/tmp/orderflow-checkout-request.json \
  -o /tmp/orderflow-checkout-response.json "$BASE/api/orders"
```

The CSRF/cookie paths/names must match your authentication-runbook session. Do not use shell tracing with private variables. Read the order ID/total/deadline from the response and GET that Location as the same Customer. Repeat the same POST/key/body and compare the saved response body with `cmp`; change quantity under that key for 409. Use another Customer to GET the order and expect 404. Duplicate the product line or inject a total/price/status field for 400. For a new key, request more availability than remains and verify no new order/reservation/event and no inventory delta. The automated tests supply deterministic barriers for genuine contention; manually launching two curl commands is not concurrency proof.

## Automated evidence and limits

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore
# Focused checkout, including real PostgreSQL and pure domain rules:
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Checkout'
# Unit/HTTP/policy subset without Docker/PostgreSQL:
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

Docker/Testcontainers is required for Category=PostgreSQL unless the private actual-server override is supplied. Each case uses a migrated independent database; no InMemory fallback. Gates before claim INSERT (same-key races) or inventory FOR UPDATE (last-unit race) prove distinct contenders reached the actual contested operation. In the last-unit case, preceding claims are distinct and catalog SHARE locks are compatible, so neither blocks its peer from reaching the inventory barrier. No barrier is placed behind an exclusive lock its peer needs. The last-unit test uses two hosts and verifies one order/reservation/event, OnHand=1/Reserved=1 and ledger links. Partial-stock and mandatory-outbox failures verify all durable stores roll back and a corrected request can reuse its key. A one-way pause tests logical time after lock acquisition and association revalidation without sleeps.

The replay test modifies only status/version through test-owned SQL to stand in for a later writer; it proves response/current-read separation, not a payment/fulfillment workflow. Unknown/503 failure injection uses a trigger only in the test database. No unauthenticated production test route or production test hook is introduced.

Trade-offs: shared catalog locks and sorted balance locks make correctness explainable but increase contention; up to 100 per-item saves add roundtrips. Successful replay retention consumes storage. Host clock synchronization remains necessary. Full cancellation/release/consume/expiry, crash/uncertain-commit fault injection, order listing/Admin workflows, payment, worker dispatch and least-privilege deployment remain separate work.
