# Admin workspace

This is the local React workspace under [ADR-030](adr/0030-admin-workspace.md), using the existing same-origin API, Identity cookies and antiforgery protection. Follow [frontend setup](frontend.md), [authentication/Admin provisioning](authentication.md) and [persistence](persistence.md) first. There are no default credentials or simulated successful API responses.

```bash
npm --prefix client ci
npm --prefix client run build
npm --prefix client run dev
```

Start the separately configured API as described in the frontend runbook. Sign in with an explicitly provisioned Admin and open `/admin`; the sidebar link uses verified roles for navigation only. Direct API requests still require the server Admin policy. No new migration is needed; latest remains `20261113082834_AddReportingReadIndexes`.

## Views and acceptance

| Route | Behavior to verify |
| --- | --- |
| `/admin/products` | Keyword/name/SKU, category UUID, sort and deterministic paged catalog; active and archived rows; create product |
| `/admin/products/:id` | Edit name/description/positive USD price/category/active flag; immutable SKU and expected version; archive/restore |
| `/admin/categories` | Keyword/pagination, create/edit/archive/restore; category choices in product forms also paginate |
| `/admin/products/:id/stock` | Current OnHand/Reserved/Available/revision, receipt or signed nonzero adjustment with reason, immutable paginated movements |
| `/admin/orders` | Exact order UUID lookup, current status/customer UUID filters and pagination; unsupported keyword/email search is not offered |
| `/admin/orders/:id` | Immutable item prices/totals, state/version/history, only next allowed fulfillment transition with optional reason |
| `/admin/reports` | UTC paid sales/captured/refunded/net, daily totals, counts by current state, top products and paged low available stock |
| `/admin/audit` | Bounded UTC/paged order history, payment attempts, captures, refunds, stock movements; safe columns only and no edits |
| `/admin/emails` | Paged email failures, safe metadata, eligible audited same-identity retry with reason/uncertainty acknowledgement |

Global failed-job/outbox/refund list and retry endpoints do not exist. The workspace therefore exposes only email failures and per-order read-only refund status. It adds no arbitrary worker/job execution, payment/refund retry, forced payment status or SQL editor.

1. Create a category/product, edit its exact price, archive/restore both and compare public visibility. Product detail server cache still follows [ADR-025](adr/0025-bounded-catalog-cache.md); Admin editing bypasses that cache. An untouched open read can age until refresh/focus/remount; no instant cross-tab/node UI invalidation is promised.
2. Receive stock with a reason, then make a signed adjustment. Create a real Customer pending order to reserve units; try reducing OnHand below Reserved. Expect 409 with trace/code, unchanged balance and no extra movement. Stale reads/disabled controls never replace database checks.
3. Open an order in PendingPayment. It has no Admin payment/fulfillment action. Settle through the configured provider callback/reconciliation/operator workflow (see [payments](payments.md)); then move Confirmed → Processing → Shipped → Completed. Try a stale version or direct skipped transition: server rejects it even if browser HTML is changed. Existing Customer cancellation/automatic expiry remain separate flows.
4. Open financial audit for that order and inspect history/captures/refunds. Try PUT against an audit route: no edit endpoint exists. Use date ranges/pagination and verify current observation timestamps. Raw provider messages, email body/recipient and provider keys are not rendered.
5. Reports use UTC `[from,to)`, max 366 days. Choose `2035-01-01` to `2035-01-02` for the browser fixture, not local date midnight. UTC midnight is 07:00 in Vietnam. Timestamp text uses device time. Paid sales, all verified captures, only succeeded refunds and net are distinct; late/unallocated captures need not be paid sales. Completed and created counts do not prove payment. Low stock is current OnHand − Reserved for active catalog, independent of dates. Each report has its own ObservedAt; use Refresh reports for new snapshots.
6. In another Admin session edit the same product/order first, then submit a stale version. Confirm useful conflict/code/trace survives editor refresh. No optimistic stock/payment/order success should appear. Failed reads show errors/retry rather than empty success.
7. Sign out/switch to a Customer, including with forged persisted role fields. Admin navigation/content is gated; direct Admin catalog/stock/orders/reports/audit/email GET/POST requests return 403. Existing owner-only customer APIs still protect other customers' data. Test Back/reload and two tabs.
8. Test 320/375/768/1440px, keyboard labels/buttons and tables. Narrow tables stack; larger dense tables scroll within a labeled keyboard-focusable region. Loading, genuine empty, validation and transport/conflict errors have explicit states.

## Recovery after an uncertain Admin write

Before sending, the browser saves one allowlisted command/body under `orderflow-admin-command:v1:<userId>` with a 12 KiB UTF-8 bound. It stores no auth token or response cache. Same-origin scripts/storage access can read it; this is not encrypted storage. It is intentionally retained on logout for the same Admin to recover. Another account's UI does not load it. The cart's existing shared-device behavior is independent.

While sending, controls show waiting and block another write. On timeout/transport loss/5xx, keep the journal and original identity. Refresh affected views and use **Recover same stock/email operation**. This sends the original operationId, quantity/delta/reason or email acknowledgement unchanged; it does not create another stock movement or retry audit. Current stock is fetched separately from the replay's historical immutable result. After a real 4xx rejection of a fresh command, correct the input; a rejection after a prior unknown outcome keeps recovery for inspection.

Catalog and fulfillment commands have no general replay endpoint. Inspect resource/version/history first. In particular, category names are not unique, so repeating an uncertain category create can duplicate a category. Explicitly check the warning and **Close local Admin recovery** only after inspection; closing does not roll back an effect. Then a new submission is a new intention. Clearing browser data cannot recreate a lost identity.

Web Locks coordinate same-origin/user tabs, and a synchronous instance guard blocks duplicate submits before disabled controls render. Missing locks, denied/full/corrupt/future/oversized storage or loss of an unresolved record fails closed. Separate devices/independent intentions remain coordinated by PostgreSQL operation identity/version/state rules. There is no automatic POST retry or promise of exactly-once external work.

Email retry accepts only eligible NeedsReview work, requires reason and explicit duplicate-risk acknowledgement when uncertain, preserves original delivery/provider identity and lifetime attempt count, and audits a single new retry cycle. Replaying its same operation after Pending/Sent returns current metadata. A new operation against Pending/Sent fails. SMTP may already have accepted mail before the original response was lost, so duplication risk remains. There is no UI to reset counters or retry a successful refund/payment. See [email recovery](email-delivery.md).

## Repeatable tests

Install the matching Playwright Chromium and satisfy existing Docker/Testcontainers prerequisites. The harness owns isolated merchant/simulator DBs, API/Redis/Vite/browser processes; no developer database is reset or seeded.

```bash
npm --prefix client run test
npm --prefix client run format:check
cd client
npx playwright install chromium
cd ..
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet test tests/OrderFlow.IntegrationTests/OrderFlow.IntegrationTests.csproj \
  -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~CheckoutBrowserTests&DisplayName~admin_'
```

Six real browser cases cover catalog lifecycle, reserved-stock rejection and committed-response-loss replay/account isolation, actual payment-to-completed fulfillment/reports/audit immutability, stale versions/direct Customer denials, email retry identity/counters/audit after response loss, and empty/error/pagination/responsiveness. Browser traces/video are off. Test-only email-review preparation and worker/clock controls require a random test nonce and are never mapped in the normal API. Response loss does not fabricate success: route.fetch first commits the real response before aborting delivery. UI recovery gates wait for actual failure/route readiness, not arbitrary sleeps.

Artifacts: `/tmp/orderflow-admin-ui/{artifacts,results,browser-artifacts}`. Exact current checks and remaining limits are recorded in [implementation status](implementation-status.md). Full WCAG/screen-reader, Safari/Firefox, production load, deployed proxy/TLS/shared keys and cross-device recovery remain unverified; this milestone does not deploy the SPA or create generic operational backlog APIs.
