# Admin reporting and audit — milestone 24 backend

Business definitions and consistency contract: [ADR-026](adr/0026-direct-admin-reporting.md). Reports read authoritative PostgreSQL directly; no event projection/worker/cache is required. Browser access uses the existing Identity Admin cookie. Customer ownership never grants Admin report/audit access.

## Routes

All routes begin `/api/admin`; GET only and no-store. `from`/`to` are required `yyyy-MM-dd` UTC dates, inclusive/exclusive, at most 366 days. Paging uses page=1/pageSize=20 defaults, maximum 100, validated offset. Per-order missing IDs return 404 after Admin authorization. Anonymous gets 401, normal Customer gets 403.

| Route | Inputs / result |
| --- | --- |
| `/reports/sales` | from/to; daily zero-filled USD PaidSales/CapturedRevenue/Refunds/NetRevenue/PaidOrderCount and totals |
| `/reports/order-counts` | from/to; all seven current status counts for orders created in range, including zero counts |
| `/reports/top-products` | from/to, limit=10 (1–100); paid units/value using order-item snapshots, current display names |
| `/reports/low-stock` | threshold=5 (>=0), page/pageSize; current active catalog Available<=threshold, sorted Available/ID |
| `/audit/orders/{orderId}/history` | from/to, page/pageSize; state/actor/reason/version/time, newest first |
| `/audit/orders/{orderId}/payment-attempts` | from/to on CreatedAt, page/pageSize; safe local IDs/status/USD amount/created/updated/attention |
| `/audit/orders/{orderId}/captures` | from/to on RecordedAt, page/pageSize; local IDs/amount/allocation/recording/provider occurrence time |
| `/audit/orders/{orderId}/refunds` | from/to on intention CreatedAt, page/pageSize; current status/amount/local IDs/created/updated/succeeded |
| `/audit/stock-movements` | from/to on OccurredAt, optional productId/orderId, page/pageSize; existing immutable movement DTO |

Existing `/admin/orders/{id}` and product-scoped [inventory history](inventory.md) remain available. These new audit endpoints add bounded dates/paging and safe payment/refund metadata; there is no PATCH/PUT/DELETE audit feature. SQL history/capture/snapshot immutability remains enforced.

Every response includes `observedAt`, sampled from the server's injected UTC clock before queries. It is observation metadata, not an exact commit timestamp/projection checkpoint. Repeatable Read keeps count/page and multiple statements inside a response consistent. Separate endpoints are separate snapshots. Status counts and audits expose current persisted status, not time travel at `to`; reporting never expires/releases/orders or reserves stock.

## How to interpret money

- **PaidSales**: winning allocated sales only, dated by atomic capture/confirmation RecordedAt, using immutable line prices/totals. Completed is fulfillment, not payment; Confirmed/Processing/Shipped sales count too.
- **CapturedRevenue**: all verified stored capture amounts recorded in range, including unallocated late/surplus/amount-mismatch captures. Created orders or observed-success attempts are not captures.
- **Refunds**: completed Succeeded refunds by SucceededAt. Pending/NeedsReview are exposed in audit but not deducted yet.
- **NetRevenue**: CapturedRevenue minus Refunds in range. A later refund can make a single day negative. This is net recorded funds, not paid sales/profit/fees.

Example test: paid 25.00 + 0.30 on January 2; late cancelled-order capture 12.50 that day, refunded January 3. January 2 has PaidSales=25.30, CapturedRevenue=37.80, Refunds=0.00, NetRevenue=37.80. January 3 has PaidSales=0.00, Refunds=12.50, NetRevenue=-12.50. Across both days NetRevenue=25.30; compensation never subtracts from paid sales. All money is an invariant string with exactly two fractional digits. Large aggregate values are decimal and may exceed a single order's amount limit; overflow errors are visible, never truncated.

## UTC and UI dates

`from=2035-01-02&to=2035-01-04` means `[2035-01-02T00:00:00Z, 2035-01-04T00:00:00Z)`. A capture exactly at from counts; one at to does not. ProviderOccurredAt is optional audit evidence and does not move locally recognized revenue across days. Refunds use the locally observed SucceededAt, not creation or an invented provider time.

UI date controls must explicitly say **UTC**. In Asia/Ho_Chi_Minh, a UTC day displays as 07:00 to next-day 07:00. Displaying individual timestamps in local time is fine; do not silently interpret the selected local midnight as UTC. An inclusive end-date picker adds one UTC calendar day to send exclusive `to`. This API does not support arbitrary local-calendar-day intervals. Response timestamps are UTC Z/microsecond strings. Report UI remains deferred.

## Setup and manual checks

Use [persistence setup](persistence.md) and private connection configuration. Review/apply the index-only migration through the existing explicit migration process; hosts do not auto-migrate:

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api
dotnet run --project src/OrderFlow.Api --no-launch-profile
```

The database-update command is an operator step, **not run against your developer data by this milestone**. Ordinary index creation can block concurrent writes on large tables; schedule it appropriately. No credential belongs in source or copied curl commands.

1. Log in with the existing Admin cookie flow. GET `/api/admin/reports/sales?from=2035-01-02&to=2035-01-04` and the other routes above. Use a date interval containing your own simulated payments; fixture dates are illustrative.
2. Create/pay an order using the simulator, advance fulfillment, edit catalog price, then refetch. Paid totals use the order snapshot and do not change with catalog prices or Completed status.
3. Exercise the existing late-payment/refund workflow. Observe capture and refund on their respective recording/completion UTC days. Pending refund is visible in audit while completed refund sums remain zero.
4. Reserve stock; compare low-stock Available to OnHand-Reserved. Archive the category to hide its products from actionable low stock; historical paid sales remain.
5. Log in as a normal Customer: all reporting/Admin audit routes return 403, including its own order ID. Existing other-customer order/payment endpoints still return 404. Audit PUT/DELETE routes are absent.

## Executable checks and plans

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Reporting' --logger 'console;verbosity=normal'
dotnet ef migrations has-pending-model-changes \
  --project src/OrderFlow.Application --startup-project src/OrderFlow.Api
```

Tests need actual Docker/PostgreSQL (or the documented private administrator test connection); no Redis/RabbitMQ/SMTP is needed for this module. Per-case generated databases are migrated/disposed by the existing harness. Fixtures control UTC with TimeProvider, use real cookies/CSRF and production business paths, refresh authentication after logical multi-day jumps, and never use sleeps for financial boundaries or races.

`ReportingQueryPlanTests` saves actual full report SQL plans and a separate 100,000-row access-path comparison to `/tmp/orderflow-reporting/plans`. Synthetic rows cover 365 dates/10,000 orders with row padding: one-day range 274 rows, one order 10 rows. The first measured plans changed Seq Scan to Bitmap Index/Heap Scan for date-range, order audit and partial succeeded-refund paths. Timing is fixture-specific; no production percentage/speed claim. Existing PK/FK/status indexes are reused; five measured-pattern indexes are added, no speculative low-stock expression index. Exact execution evidence is in [status](implementation-status.md).

Observed full-query access paths in the focused financial-fixture run (PostgreSQL 18.6; optimizer choices are data-dependent):

| Actual SQL | Observed path |
| --- | --- |
| Daily finance/sales | IX_PaymentCaptures_RecordedAt_Id; IX_Refunds_SucceededAt_Id; PK_OrderItems via nested loop; grouped CTE branches |
| Created-cohort counts | Bitmap scan of existing IX_Orders_CreatedAt_Id, then group by status |
| Top paid products | Capture date index, PK_OrderItems and PK_Products |
| Low-stock count/page | Small Category/Product scans joined to PK_Balances, then availability filter/sort; no expression-index inference from two products |

Trade-offs: live reports add DB read work and repeatable-read snapshots; 366-day and 20s statement bounds do not guarantee constant cost. Wider date ranges may legitimately scan, and low-stock filters/sorts current balances. Five indexes add write/storage cost. Separate endpoint snapshots can differ. No reporting replica/projection, historical state/stock reconstruction, UI/export, runtime-role deployment or production load benchmark is included. Existing production DB privilege hardening remains a later milestone.
