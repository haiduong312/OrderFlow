# Inventory — milestone 08

Inventory owns PostgreSQL balances and append-only StockMovements. Admin can receive physical stock and apply signed adjustments; Orders/reservations arrive in 09. [ADR-013](adr/0013-inventory-ledger-and-stock-commands.md) records the scope/coordination decisions. Existing [Identity cookie/CSRF](authentication.md) and [resource authorization](authorization.md) protect admin routes.

## Initialization and invariants

Every Product has one inventory.Balances row keyed by ProductId, with integer OnHand/Reserved and bigint Revision. Database constraints require OnHand >= 0, Reserved >= 0, Reserved <= OnHand and Revision >= 0. Available = OnHand - Reserved is computed server-side and never writable. Public/admin Product DTOs expose Available; full OnHand/Reserved/Revision is available through the Admin inventory endpoint. Stock changes do not alter Catalog's version.

Migration 20261104083112_AddInventory backfills all existing products, including archived ones, at OnHand=Reserved=Revision=0. A Product AFTER INSERT trigger initializes every new product/balance/zero movement atomically. Initial history is Kind=Initialization, ActorKind=System, ActorUserId=null, fixed reason "Product inventory initialized at zero", Operation=InitializeInventory:v1 and OperationId=ProductId. OccurredAt is the product's logical CreatedAt, not a fabricated physical receipt time. This system action is explicit, not a fake default user. Migration backfill invents no stock; Admin must record real receipts after counting/receiving it.

Product creation rollback also rolls back its balance and opening movement. Reads never create missing inventory or return a made-up zero fallback. Existing products need the dedicated migration applied before serving the new app; API/worker never migrate automatically.

## API and validation

All routes start /api/admin/products/{productId}, require the existing AdminOnly policy; POST also requires the antiforgery cookie/request-token pair.

| Method/suffix | Input | Response |
| --- | --- | --- |
| GET /inventory | Product UUID | 200 InventoryResponse: productId/onHand/reserved/revision/available |
| GET /stock-history | page=1/pageSize=20, maximum 100, validated offset | 200 {items,page,pageSize,totalCount}, Revision DESC |
| POST /stock-receipts | operationId UUID, positive quantity, reason | 201 immutable movement + history Location |
| POST /stock-adjustments | operationId UUID, nonzero signed delta, reason | 201 immutable movement + history Location |

Reason is required, trimmed and 1–500 characters; HTTP raw length is bounded too. Receipt/adjustment quantity is a JSON integer, never fractional or a quoted number. ActorUserId comes only from authenticated ICurrentUser and a real Identity user; clients cannot set actor, kind, OnHand, Reserved, Available, Revision or occurrence time. Unknown body properties are rejected. Product DTO updates still cannot mutate stock.

Anonymous admin calls return 401, wrong role 403, missing/bad CSRF 400 antiforgery_failed. Invalid quantity/delta/reason/operation ID is 400 validation_failed. Missing product/inventory is 404. A removal below Reserved (including below zero) is 409 insufficient_stock; exceeding int.MaxValue stock or bigint revision is 409 stock_limit_exceeded. Unknown infrastructure errors remain sanitized 500 and are logged as failures, not turned into stock conflicts/empty success. Archived Product/category stock remains manageable by Admin; this never restores public product visibility.

An adjustment changes OnHand only; Reserved is untouched. Stock reserved by future order workflows cannot be removed by an adjustment. Example: OnHand=10, Reserved=7, Available=3: delta=-4 fails, delta=-3 succeeds and leaves Available=0. Current production code has no reservation endpoint; tests use scoped SQL fixture setup to exercise this existing-balance boundary.

## Durable operations and history

Use a fresh operationId for each new intended receipt/adjustment; retain it and the body until the outcome is resolved. Receipt/Adjustment share the ChangeStock:v1 namespace. Same ID + normalized actor/product/kind/delta/reason returns original 201 movement; whitespace around reason is normalized. Changed body/actor/product/kind is 409 idempotency_conflict. The replay is the old movement snapshot, not today's stock balance. Failed transactions leave no movement/claimed ID; retrying an uncertain commit with the same ID reconciles against the durable record. Do not generate a fresh ID automatically after a lost response.

Movement fields include movementId, productId, operation/operationId, Kind, ActorKind/ActorUserId, reason, OnHandDelta/ReservedDelta, before/after balances, revision and occurredAt. Deltas/snapshots and kind/actor shape have DB checks; User/Product references are restrictive. Initialization revision=0; each successful change adds the next revision. History ordering uses unique (ProductId,Revision), so equal timestamps and wall-clock skew do not reorder durable effects. UTC JSON timestamps use Z and six fractional digits; quantities/revisions remain numbers and enum names are strings.

PostgreSQL triggers reject UPDATE/DELETE/TRUNCATE on movements; EF also prevents modifications of saved movement properties. Correct mistakes by a new reasoned adjustment, never editing the ledger. The schema owner could intentionally change/drop these protections; deployment runtime privileges still need the planned least-privilege setup. No production history-edit/delete endpoint exists.

## Transactions and tracked state

Each stock command opens one database transaction, locks its balance FOR UPDATE, samples injected TimeProvider, applies domain checks, saves balance + movement and commits. No external network call occurs in it. Product creation uses its own transaction with the initialization trigger. Expected unique-operation races roll back before reading the winner; no query is made in an aborted transaction.

InventoryLocking.LockAsync supports 1–100 distinct product IDs. It deduplicates, sorts canonical UUID bytes, then acquires row locks sequentially. Every future multi-row Inventory operation must pass its complete target set in one locking call and use the same order; no batch mutation HTTP API is added now. Global ordering is category/product before inventory. Stock-only changes never acquire a later Product/Category lock, avoiding reverse-order cycles.

Balance writes use tracked EF entities under locks, not ExecuteUpdate. A prior Unchanged tracked balance is explicitly refreshed (current/original snapshots, state reset) from a fresh locking query. Pending unsaved edits to a target balance are rejected; they are not silently discarded. This prevents stale read values from overwriting another process's committed stock. A failed cross-product duplicate discards its new movement and reloads its rolled-back balance before returning a conflict/replay.

Count/page history reads are bounded/no-tracking and sequential, not an atomic snapshot while new movements arrive. Available is advisory; checkout must later enforce reservation eligibility inside its own transaction. No full reservation ledger/state machine, stock transfer/batch UI, outbox dispatch or background worker is added in this milestone.

## Manual verification

1. Follow README restore/build and [PostgreSQL setup](persistence.md). Apply the dedicated migration step: dotnet ef database update, including 20261104083112_AddInventory. Do not automatically reset data. Existing products become zero-inventory rows with explicit system opening facts.
2. Follow [auth](authentication.md) to seed an explicitly enabled Development Admin from private config, log in with a cookie jar, obtain a fresh CSRF token and keep the temporary variables/function until this walkthrough finishes. Create a Category/Product via [Catalog](catalog.md), then assign its returned ID:

```bash
ORDERFLOW_PRODUCT_ID='<returned-product-uuid>'
ORDERFLOW_OPERATION_ID=$(python3 -c 'import uuid; print(uuid.uuid4())')
ORDERFLOW_CSRF=$(orderflow_csrf)
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" \
  "$ORDERFLOW_AUTH_URL/api/admin/products/$ORDERFLOW_PRODUCT_ID/inventory"
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" \
  -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $ORDERFLOW_CSRF" \
  --data "{\"operationId\":\"$ORDERFLOW_OPERATION_ID\",\"quantity\":10,\"reason\":\"Supplier delivery\"}" \
  "$ORDERFLOW_AUTH_URL/api/admin/products/$ORDERFLOW_PRODUCT_ID/stock-receipts"
# Repeat exactly the same ID/body: original 201 movement, OnHand remains 10.
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" \
  "$ORDERFLOW_AUTH_URL/api/admin/products/$ORDERFLOW_PRODUCT_ID/stock-history?pageSize=20"
curl --fail-with-body -i "$ORDERFLOW_AUTH_URL/api/products/$ORDERFLOW_PRODUCT_ID"
```

3. With a new operation UUID, POST /stock-adjustments with a valid JSON body containing the new operationId, delta=-3 and reason="Damaged units"; expect OnHand/Available=7, a new revision and movement. Delta=-8 now gives 409 with balance/history unchanged. Same old operation ID with a changed body gives 409. Missing CSRF gives 400; Customer gets 403; actorUserId/available in body gives 400. Fetch /inventory for current stock after replay rather than treating an old movement snapshot as current.
4. Run PostgreSQL-backed tests with private ORDERFLOW_TEST_POSTGRES:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore
# Inventory tests only (plus any existing test names containing Inventory):
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Inventory'
```

Use the matching --artifacts-path across restore/build/test if building outside the repo. No credential appears in tracked examples. No real sleeps establish races: tests synchronize distinct request DbContexts before contested locks/inserts and verify every participant. Evidence/limits are recorded in [implementation status](implementation-status.md).
