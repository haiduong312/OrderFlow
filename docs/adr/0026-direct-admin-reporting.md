# ADR-026 — direct Admin reporting and read-only audit queries

Date: 2026-11-13. Status: accepted for milestone 24 backend.

## Scope correction

The original specification/ADR-001/ADR-005 and roadmap 24 planned event-fed Reporting snapshots/checkpoints. The latest request prioritizes simple PostgreSQL/EF queries and says not to introduce an event-sourced reporting platform. This contradiction was reported before implementation. Supersede only the reporting projection/consumer/checkpoint requirement: use direct authoritative read models, without reporting tables, event handlers, replay jobs, caches or background services. Notification/payment messaging guarantees remain unchanged. The backend does not depend on the unimplemented frontend milestone 20; report UI remains deferred.

## Semantics

All report ranges are required ISO UTC **dates**, `[from,to)`, `from < to`, maximum 366 days. Dates denote UTC midnight, never browser-local midnight. Finance timestamps use **local verified recording/observation time**, not untrusted provider occurrence time:

| Measure | Source and period membership |
| --- | --- |
| PaidSales | Sum immutable OrderItems.LineTotal for **allocated winning captures**, by Capture.RecordedAt. That timestamp is also the confirmation decision time in the existing atomic settlement. Includes Confirmed/Processing/Shipped/Completed; current fulfillment status does not redefine payment |
| PaidOrderCount | Count allocated captures in range; unique allocated OrderId prevents duplicate winning orders |
| CapturedRevenue | Sum **all** stored verified PaymentCaptures.Amount by RecordedAt, including late/surplus/mismatched-amount captures needing compensation. Merely Queued/Pending/observed-success attempts and created orders contribute nothing |
| Refunds | Sum Refunds.Amount only where Status=Succeeded, by SucceededAt. Pending/NeedsReview intentions are not completed cash refunds |
| NetRevenue | CapturedRevenue minus completed Refunds in the same interval. Can be negative when a refund falls after its capture. This is recorded net captured funds, not profit, recognized paid sales or net of gateway fees |
| Order counts | Orders **created** in range, grouped by current persisted Status at the read snapshot. Not historical status at `to`, not transitions during the interval, not a paid-order count. Reads do not expire overdue PendingPayment orders |
| Top products | Allocated sales in the capture interval, sum immutable line quantities/LineTotal; rank units DESC, paid sales DESC, ProductId ASC, limit 1–100. Current catalog name is a display label; SKU is immutable. Archived products remain in historical sales |
| Low stock | Current active products in active categories with `OnHand - Reserved <= threshold`, including equality/zero. Paginated, ordered Available ASC/ProductId ASC. Not a historical inventory reconstruction or stock reservation |

Refund compensation of a late capture is **not negative paid sales**: the late capture never won allocation. It appears in CapturedRevenue and eventually Refunds; a completed compensation nets to zero over a range covering both facts. No customer return/refund feature is invented. Public monetary fields are invariant decimal strings with exactly two fractional digits; report net strings permit `-`. SQL uses numeric aggregates; .NET uses decimal, never float/double. Report sums can exceed one order's numeric(18,2) bound; they are not forced through the nonnegative UsdMoney primitive. Numeric/decimal overflow fails visibly, without truncation or fake zero.

## Implementation and consistency

ReportingService uses parameterized EF `Database.SqlQuery<T>` for numeric/time-group aggregates, and EF projections for counts/stock. The UsdMoney scalar converter is not bypassed with invented LINQ translations. A filtered materialized captures CTE is shared by separate financial and item-sale aggregates, so a multi-item join cannot multiply captured revenue. Database aggregation precedes bounded materialization (<=366 days, <=100 top products); audit count/page queries only project whitelisted fields. Cancellation propagates. No new package/version is needed.

Each response uses one scoped DbContext, a PostgreSQL **Repeatable Read, READ ONLY** transaction, 5s lock/20s statement budgets and cancellation. Dispose closes the read transaction; there are no business writes to commit. `observedAt` is the injected UTC clock sampled immediately before report queries, not a provider timestamp, replication watermark, projection checkpoint or exact DB commit cutoff. The snapshot is established by the first SELECT. Count/page and all aggregates within that response agree; distinct endpoint requests can observe different commits. No shared DbContext/concurrent Task.WhenAll queries or external calls occur in the transaction.

AdminReportsController and AdminAuditController use the existing strict AdminAccess policy and no-store responses. Anonymous=401; authenticated Customer=403, including their own order ID. Existing Customer order/payment ownership endpoints keep 404 for another owner. Audit DTOs expose local IDs, states, amounts, timestamps and existing business history/movements; no provider request/refund/capture/transaction keys, raw observations/callbacks/hashes, leases, signatures, credentials or account emails. There are GET routes only, no audit edit endpoint; existing SQL immutable-history/financial guards remain intact.

Audit ranges filter history/movements by OccurredAt, attempts/refund intentions by CreatedAt and captures by RecordedAt. Refund/attempt statuses are current at observation, even when their last update is outside the selected creation range. Per-order routes return 404 for nonexistent orders after Admin authorization. Stock audit optional IDs filter results; unknown filter IDs yield an empty page. Stable timestamp/local-ID tie breakers and shared paging (20 default, 100 max, checked offset) apply.

## Index evidence and migration

Before choosing indexes, real PostgreSQL EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) compared scan/index access on a disposable **100,000-row**, 365-day, 10,000-order synthetic planner fixture (200-byte row padding). One-day range selected 274 rows; one-order lookup selected 10. Baseline Seq Scans removed 99,726/99,990 rows; candidate plans used Bitmap Index/Heap Scans. Plans are saved under `/tmp/orderflow-reporting/plans`. This tests access patterns and selectivity, not the entire production workload or a latency improvement percentage. A separate test EXPLAINs all five actual report SQL statements over the real financial correctness fixture and asserts no tracked entities; tiny-fixture scans are legitimate. Re-measure with production-like cardinalities/statistics before adding more indexes.

Migration **20261113082834_AddReportingReadIndexes** adds only:

- Captures(RecordedAt,Id): finance/date access.
- Captures(OrderId,RecordedAt,Id): all captures for order audit; the old unique allocated-only index cannot serve unallocated captures.
- Attempts(OrderId,CreatedAt,Id): all attempt states for audit; old active-only uniqueness cannot serve terminal attempts.
- Refunds(SucceededAt,Id) WHERE Status='Succeeded': completed-refund date sums.
- StockMovements(OccurredAt,Id): cross-product bounded time audit.

Reuse existing Orders(CreatedAt,Id)/status indexes, OrderItems(OrderId,ProductId), OrderStatusHistory(OrderId,Version), Refunds(CaptureId) uniqueness and StockMovements(ProductId,Revision). No speculative low-stock expression/covering index. Ordinary CREATE INDEX adds write/storage cost and can block writers during migration; no developer DB is migrated automatically. Populated previous-schema upgrade tests preserve financial identities/totals and verify all five indexes. No business table/column/trigger change.

## Verification / limitations

Deterministic business-time fixtures use actual checkout, fulfillment, settlement and refund domain paths. Tests cover a capture one microsecond before from, at from and at exclusive to; capture/creation date differences; completed vs paid; 0.10 × 3 exact totals after catalog price/name changes; late compensation; Pending/NeedsReview refund exclusion; signed negative net; low-stock reserved quantities/category archival; paginated secret-free Admin audit and Customer denials. A TCS gate after the first snapshot SELECT pauses history counting while an independent Admin commits a transition; the first page remains consistent, the next request sees the new history. No race sleeps.

No reporting projection exists, so there is no reporting inbox/as-of checkpoint/replay protocol to test. Existing authoritative unique captures/refunds prevent duplicate financial facts; a source-settlement replay test proves repeat reporting does not count another capture/refund. This does not claim transport exactly-once. Historical point-in-time statuses/stock, local-calendar-day intervals, report UI, exports, arbitrary filters, replicas/warehouses and production load benchmarking are outside scope. See [runbook](../reporting.md) and [executed status](../implementation-status.md).

Official references: [EF parameterized SQL and unmapped DTOs](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries), [PostgreSQL EXPLAIN](https://www.postgresql.org/docs/18/sql-explain.html), [Repeatable Read](https://www.postgresql.org/docs/18/transaction-iso.html#XACT-REPEATABLE-READ).
