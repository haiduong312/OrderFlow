# React frontend — shopping, checkout, payments and live updates

Implemented pages: overview `/`, sign-in `/login`, registration `/register`, current user `/account`, Customer `/orders`, `/orders/:id`, `/checkout`, authenticated `/notifications`, public `/products` and `/products/:id`, and `/cart`. Session contract: [ADR-027](adr/0027-react-session-foundations.md); browsing/cart contract: [ADR-028](adr/0028-catalog-cart.md). Recoverable checkout/payment/notifications/realtime: [ADR-029](adr/0029-checkout-recovery-and-live-ui.md). Admin/report UI and production deployment remain later work.

## Run locally

Use Node **24.19.0** (`client/.nvmrc`) and npm **11.17.0**. All direct versions and npm lockfile are pinned; React 19.3.1, TypeScript 7.0.2, Vite 8.3.8, TanStack Query 5.107.1, Zustand 5.1.0 and Router 8.5.1 were checked against their stable npm metadata/peer engines. Official SignalR JS client 10.0.11 is now also pinned; no .NET package/version changed.

First follow [persistence](persistence.md) and [authentication](authentication.md) for private DB configuration, the dedicated migration command and role provisioning. Hosts never auto-migrate or create credentials. Existing backend routes remain the authority; an unconfigured database produces real errors, not demo success.

Terminal 1, repository root (explicit loopback Development HTTP):

```bash
# Privately configure ConnectionStrings__OrderFlow first, apply migrations and seed roles as documented.
DOTNET_ENVIRONMENT=Development Authentication__AllowInsecureHttp=true \
  dotnet run --project src/OrderFlow.Api --no-launch-profile -- --urls http://127.0.0.1:5080
```

Terminal 2:

```bash
cd client
npm ci
npm run dev
```

Open the printed loopback URL (normally `http://127.0.0.1:5173`). The browser uses relative `/api/...` URLs on **that same origin**. Vite proxies them to `http://127.0.0.1:5080`; no CORS setup, cookie-domain rewriting, JWT or client auth token is involved. To change the API target, set a non-secret process environment variable before starting Vite:

```bash
ORDERFLOW_API_TARGET=http://127.0.0.1:5081 npm run dev
```

For a trusted HTTPS target, preserve certificate validation (trust its public development CA through your Node/OS setup); do not add `secure:false`. Browser Secure-cookie behavior must match the browser's own HTTPS origin. The documented HTTP option is local Development only and retains the backend's explicit guard. Production requires a same-origin proxy/static/history-fallback setup in 25/26; Vite development/preview is not a production host.

## Session behavior

Startup confirms the cookie with `/api/auth/me`; protected routes use that verified in-memory session. Focus/restore and identity changes recheck it, and current-scope private API 401s expire it. A real 401 goes to sign-in; network/500 errors show unavailable with retry and original error code/trace when supplied. Successful registration creates a Customer **without login**; continue through sign-in. Login and logout use fresh antiforgery pairs, clear private browser state before changing identity, then verify `/me` and refresh request-token state. CSRF 400 is shown, not blindly replayed; the next explicit submit gets a fresh token. Server field messages appear beside labeled inputs, including password policy failures; invalid credentials remain generic.

Account switch is the existing sign-in endpoint, available from Account → Switch account. Before its request, private data is hidden/cancelled/removed. Failure rechecks the surviving cookie and still shows the original failure; no false sign-out success. Back navigation/protected routes do not grant access. Current-user roles are display/navigation hints only; API roles/ownership are authoritative. A normal Customer cannot read Admin reports even if localStorage is edited.

Session/CSRF/passwords/private query data are never persisted. UI preferences use `orderflow-ui-v1`, containing version + `state.density`; unknown fields are ignored and the next write strips them. Cart persistence separately stores only IDs/quantities under `orderflow-cart`; authenticated pending-operation recovery has its own user-scoped keys (see below). Drawer state is not persisted. Cookie tickets are HttpOnly; JavaScript receives only an in-memory antiforgery request token. Auth forms avoid putting passwords into a query/mutation cache. No authoritative price/stock/payment-success state is manufactured.

The QueryClient is created once, with retry=false, staleTime=30s/gcTime=5min. Orders keys contain current user ID/page/pageSize/status. `/me` is a private zero-lifetime bootstrap key while the user ID is unknown. Private success/error results are generation-fenced; a late old-user 401 cannot log out the new account. BroadcastChannel, hidden/pagehide cleanup and focus/restore verification coordinate tabs; messages contain no private user data. There is no continuous cross-device revocation or instant remote-tab pixel-erasure claim. Server requests still authorize every operation. Owner checks on returned order DTOs also detect mismatched identity before caching rows.

Orders show real API totals/status/expiry metadata and scoped pagination/status filters, with detail/payment/eligible cancellation routes. Empty states require an actual successful empty page. Money remains JSON strings and uses bigint cents for exact formatting; dates display in the device's time zone with an explicit note. No cached frontend state can reserve stock, authorize a role or confirm payment.

## Checks and real browser flows

```bash
cd client
npm ci
npm run build
npm run test
npm run format:check
npx playwright install chromium
cd ..
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet test OrderFlow.slnx -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~FrontendBrowserTests|FullyQualifiedName~BrowserControlAbsenceTests'
```

The existing PostgreSQL/Testcontainers prerequisite applies. `FrontendBrowserTests` owns a freshly migrated DB and Kestrel API for each of sixteen original session/shopping scenarios, starts Vite/Chromium through Playwright, seeds generated users/roles/opening stock and uses real cookie/CSRF HTTP for orders. Node and matching browser binaries must be available. Missing prerequisites fail visibly, without skips/fake API responses. The normal API never maps the clock control; an extra service-free test checks GET/POST 404. The clock control exists only in the test host, bound to loopback and a random in-memory header key, and advances API TimeProvider by nine hours to exercise actual ticket expiry.

Default full `dotnet test` now includes these browser tests. For backend-only workflows use `--filter 'Category!=Browser'`; the service-free filter already excludes PostgreSQL (and therefore the browser cases). For a single case add `&DisplayName~account_switch_isolation` to the fully-qualified browser filter. `npm run test:browser` is the low-level driver; run it through the .NET harness, which supplies isolated backend/fixture environment variables. It intentionally cannot silently substitute a demo API when those are absent.

Session scenarios: session restore; register/server field validation; invalid credentials/CSRF header tampering/fresh retry; missing and expired tickets; same-SPA account-switch/logout cache isolation; shared-cookie cross-tab switch/logout; deterministic pending request/real empty data/transport error/retry; unavailable `/me`; responsive keyboard/drawer/forms. Tests gate actual requests or abort transport; they never fulfill a made-up successful response. No test disables production auth/CSRF/rate limits. API hosts are isolated per case so the real per-IP quotas remain intact. Trace/video capture is off to avoid storing cookie/credential traffic; screenshots use generated example.invalid users only.

Current shopping screenshots/results are under `/tmp/orderflow-shopping/browser-artifacts` / `results`; historical session-only evidence is under `/tmp/orderflow-frontend`. Playwright defaults to ignored `client/test-results` for low-level local execution. Desktop and 375px login screenshots were visually inspected; width checks also cover 320/768/1440px. The private app has no fixture seed/control route in production and no default login/password. No developer database was migrated/reset by implementation.

## Manual acceptance and limits

1. Register a new Customer with real backend roles provisioned. Try a 12+ character password missing required character classes; verify the server field error/code/trace. Successful creation leaves `/me` unauthorized until sign-in.
2. Sign in, reload `/account`, and inspect the HttpOnly auth cookie in browser tools. API traffic should stay under the Vite origin; local/session storage contains no token or user cache.
3. Open Orders, then Account → Switch account without reloading. A second account sees only its own rows. Sign out and use Back; protected content is gated. Repeat with two tabs sharing cookies.
4. Remove the auth cookie, then change an order filter to request data: 401 routes to sign-in. Browser tests separately prove genuine server ticket expiry via logical time. Tampered CSRF headers are rejected by the real API; retry retrieves a new pair.
5. Abort/block API traffic in browser tools: loading/error/retry is explicit, not a fake empty table or successful login. Missing/expired session and unavailable infrastructure are separate states.
6. At mobile width open navigation, tab within the drawer, press Escape and confirm focus returns to its button. Forms have labels/hints/errors, skip link/route heading focus and reduced-motion behavior. Compact view is the only persisted UI preference; cart storage separately contains only IDs/quantities.

Verified Chromium/loopback development scope only. Safari/Firefox, a full WCAG/screen-reader audit, production TLS/proxy/CSP/shared keys, cross-device revocation, hostile suspended-tab scheduling, load testing and deployed SPA fallback remain unverified/deferred. No analytics, remote fonts, external media, fake sales metrics or Admin/reporting mutations are added. See [implementation status](implementation-status.md) for exact execution evidence.


## Browsing and cart acceptance

Open `/products`. Keywords are literal server name/SKU substrings, debounced 300ms (Enter submits immediately). Category/sort/page/pageSize are in both URL and Query keys; changing a filter resets page one. Category choices paginate separately in groups of 20, with Previous/Next controls. Product page sizes are 12/24/48. New keys show loading, not stale rows from another page. Same-key background refresh is labeled. Server UUID tie-breakers make stable-data pages deterministic; concurrent catalog edits may move rows between requests.

Open a detail and add a whole quantity, then `/cart`. Valid quantities are 1–1000, max 100 distinct products. Additions merge immutably; Update replaces, Remove deletes, Clear empties. These actions do not reserve/decrement stock. Quantities over observed availability remain visible with a shortage message; review cannot succeed until resolved. Out-of-stock add controls are disabled. Missing/archived detail returns an unavailable screen; cart retains that ID/quantity with Remove. HTTP/network failures have Retry and withhold a full total instead of pretending a price is zero.

Cart state is **shared by this browser/origin across accounts** and intentionally survives logout/account switch. The page labels this policy and offers Clear for shared devices. It contains no account/order data. Same-origin storage events rehydrate other tabs; simultaneous writes are last-write-wins, not transactional merging. No cross-device/account cart synchronization exists. Existing private Query session/order state is still purged/fenced at identity changes.

Storage key `orderflow-cart`, version 1, persists only `{state:{items:[{productId,quantity}]},version:1}`. Hydration bounds JSON to 16 KiB and the first 100 rows, validates UUID/integer limits and keeps the first valid duplicate ID; unknown fields are stripped. Missing/unversioned/oversized data is ignored; unsupported older/future version numbers reset to empty v1 rather than guessing a conversion. Corrupt/denied/full storage reports a safe constant warning and UI persistence notice; in-memory actions remain available. There are no stored price snapshots/totals to become authoritative.

Cart queries refetch details on mount/focus/reconnect; “Check prices & availability” deliberately refetches all selected products. Exact bigint cents derive estimated line/total values from successful current query DTOs; numeric(18,2) overflow is surfaced. A full estimate requires every row's price. Review announces old/new in-memory prices, recomputes totals and is invalidated by edits/data changes/errors/shortages. With optional backend Redis detail caching, a new check can still return catalog fields up to 30 seconds old at lookup; Available is read separately from PostgreSQL. Displayed values may age further afterward. This is not a held quote: existing CreateOrder later validates category/product/price/stock under DB locks and returns authoritative immutable line prices/totals. ADR-028 review itself never POSTs an order. ADR-029 now supplies the explicit protected checkout flow below, without fabricated checkout success.

Manual checks:

1. Filter keyword/category, change sort/page size and Next/Previous; reload/Back to verify URL state. Try a no-match keyword and Reset.
2. Add quantities 3 of a 0.10 item plus 1 of a 0.20 item: estimate is exactly 0.50. Try blank/0/1.5/1e2/1001; storage and total stay unchanged until a valid Update. Reload, Remove, then Clear.
3. In another Admin session, update a selected product's price through the existing API. Check the cart; inspect changed-price message/new exact total. Archive a selected product or its category; check/reload and remove its unavailable row.
4. Request more than Available, block a detail request, retry, and remove a missing item. No successful review/full total may hide an unresolved missing-price error. Inventory Available remains unchanged by cart actions.
5. Sign in A, logout and sign in B: the explicitly shared browser cart remains while private orders remain isolated. In a second same-origin tab, add/update/clear and observe storage synchronization. A shared-device user should Clear.
6. At 320/375/768/1440px verify controls, labels, messages and summary; keyboard users can use explicit submit/update/remove buttons. Browser screenshots are synthetic fixtures, not real customer data.

The seven shopping browser scenarios are `catalog_filter_pages`, `catalog_categories_detail`, `cart_quantities_restore`, `cart_price_change`, `cart_archived_unavailable`, `cart_account_scope`, `cart_cross_tab`. They augment nine existing session cases. Fixtures add catalog data only in owned disposable DBs; price/archive changes use real Admin cookie/CSRF HTTP. No success-response interception is used. Full-cart fan-out (up to 100 detail GETs), last-write-wins tab edits, offset movement during mutations, Safari/Firefox, production performance/TLS/proxy/deployment remain documented limits. Checkout now follows ADR-029 below.


## Checkout, payment and order recovery

Sign in as Customer, add products, Continue to checkout. The checkout page reads current catalog estimates; **Create order & reserve stock** is the explicit POST. The server validates/locks eligibility, current prices and stock, returns authoritative immutable lines/total and a 15-minute reservation. After verifying owner detail, **View order & finish checkout** closes local recovery and clears the cart only when its normalized IDs/quantities still match; a changed cart remains.

Before either checkout or payment initiation, a UUID key and minimal normalized operation are saved synchronously under `orderflow-operations:v1:<current-user-id>`. Checkout stores an exact sorted/lowercase-ID/integer JSON string + key + optional acknowledged orderId. Payment stores orderId/key/optional attemptId; body is always literal `{}` in v1. Record shape/version/IDs/body/24 KiB UTF-8 and <=20 payment entries are validated. There is one unresolved checkout per user/origin. Native Web Locks serialize tabs; unavailable Web Locks/storage block new mutations. No auth token/full DTO/price/total is stored. LocalStorage is not encrypted or protected against same-origin scripts; API ownership remains the security boundary.

A lost response, native 15s timeout, network failure, 5xx or uncertain acknowledgement keeps the original body/key and the cart. Reload or sign back into the **same account**, then Recover original checkout/payment request. It replays the exact original intention, not a modified cart. Another account does not load that recovery key. Old-account responses remain session-generation fenced. Never clear browser storage or generate a different key just because the result is uncertain.

To intentionally change the request, recover/finish the old one first. Alternatively review My orders and use the explicit checkbox + **Discard checkout recovery**: this removes only local recovery and **does not cancel an order/reservation**; a new key may create another order. Unresolved payment records have a similarly explicit discard warning, which never cancels a charge/refund. Use eligible cancellation against the actual order if desired. Invalid/lost recovery fails closed and offers an acknowledged Reset after reviewing orders; no automatic version migration drops uncertain data. Browser-data deletion/reload after deletion cannot recover a lost key magically, so inspect existing orders/support before a new intention.

Start payment on an eligible order. A 202 only records/reuses an attempt; Queued/Pending/Unknown/provider observation is not paid. Timeout shows Recover original payment request with the same key, including after expiry. A server-reported Failed attempt permits an explicit new attempt; unresolved records are not automatically replaced. Paid/Cancelled/Expired states come from GET/callback/reconciliation. `success=true`, `paid=true`, redirect or return parameters are ignored as payment evidence. No customer provider redirect URL exists in this repository.

Order detail shows server history, immutable item prices and a countdown based on server Expiry.ObservedAt/ExpiresAt plus monotonic elapsed time. Network/suspension lag makes it informational: zero does not locally mark Expired or authorize cancellation/payment. Cancel eligibility comes from server metadata and is rechecked by POST; an in-flight capture after cancellation can require compensation. All display dates say device time zone; server values remain UTC.

GET `/api/orders/<id>/payment-status?page=1&pageSize=20` is a new no-store Customer owner read. Wrong owner is 404, anonymous 401, wrong role 403. One read-only repeatable snapshot projects safe attempts/captures/refunds, total counts and observedAt, independently paging each section at the requested page (max 100). Provider keys/transaction/capture/refund identities, raw observations/bodies/leases/credentials are omitted. Separate order/status requests can observe slightly different snapshots. The UI distinguishes allocated captures from late/surplus unallocated funds and Refund Pending / Succeeded / NeedsReview. Pending/review does not mean money was returned; general customer refund/return commands remain out of scope.

## Notifications and SignalR

`/notifications` offers durable list, unread-only filter, pagination and CSRF-protected mark-read. GETs are scoped/abortable and keys include user and all parameters. Hint IDs never become unread increments or order status. The official JS client is pinned to stable **10.0.11** for the .NET 10 server.

For optional live updates, privately configure the existing API Realtime/Redis settings in [realtime setup](realtime.md), then start Vite with:

```bash
VITE_ORDERFLOW_REALTIME=true npm --prefix client run dev
```

This is a non-secret build/dev feature flag; restart/rebuild when changing it. Default off keeps checkout and durable reads available without Redis/RabbitMQ. `/api` proxy now enables WebSocket upgrade with original Host/Origin. Normal WebSocket negotiation uses Identity cookies and a fresh/current in-memory antiforgery header; no bearer factory, access_token, token storage or fallback transports. Production still needs real same-origin proxy affinity/upgrades/shared protected keys under milestone 26.

ConnectionReady/reconnected refetch active durable owner state. NotificationAvailable accepts only UUID identities, deduplicates a bounded recent set and invalidates relevant Query keys. It never copies hinted money/status into cache. Session change stops the socket immediately before auth network actions and fences old callbacks. Automatic network reconnect uses finite delays 0/2/10/30s; initial failure/exhaustion shows Offline with manual Reconnect. Intentional server close need not auto-reconnect. Order detail/payment poll every 15s while active; orders/notification/unread poll every 30s, with manual Refresh and focus/session restore catch-up as well. Background suspension/lost hints are not instant consistency or continuous revocation guarantees.

## Supported development simulator workflow

The existing simulator is an explicitly enabled **Development/Testing, loopback-only machine/operator API**, not a customer checkout web page. Follow [payment setup](payments.md), [signed callbacks](payment-callbacks.md) and [recovery setup](payment-recovery.md): separate provider DB, private API/control/signing secrets, opt-in PaymentProcessing/PaymentReconciliation/RefundProcessing worker loops, operator resolve/scenario/callback commands. Keep secrets in server/operator processes. The frontend has no simulator control form, embedded secret, invented return URL or “simulate paid” endpoint. Its simulator note is Development-only and omitted from production builds. A real provider requires its own separately specified gateway/UI contract.

## New end-to-end acceptance

The nine CheckoutBrowserTests cases are happy payment, lost create response/replay, account recovery isolation, actual 15s initiation timeout/replay, expired order, late capture/refund success, cancellation/notifications/mark-read, real socket disconnect/reconnect catch-up, and NeedsReview after a rejected refund. They augment the original sixteen session/shopping cases.

They run actual merchant Kestrel + PostgreSQL, an independent migrated simulator DB/HTTP host, production worker iterations, production notification deduplication/validated dispatcher, Redis and official Chromium SignalR/WebSocket negotiation. A nonce test-host-only driver advances logical server time/steps workers and replays real committed events; normal API GET/POST control routes are proven absent. The browser application never calls this driver. This fixture steps consumers/dispatch directly; the separate existing RabbitMQ/two-API suite covers broker plumbing. Fault interception loses a real committed 201/202 rather than manufacturing a successful response. Reconnect gates actual renegotiation after network abort and changes durable state while disconnected. Duplicate hints/source delivery remain harmless.

```bash
# Existing client dependencies + matching Chromium and Docker prerequisites apply.
dotnet test OrderFlow.slnx -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Frontend'
```

Manual acceptance: perform normal checkout/payment; interrupt POST responses and reload/recover with unchanged body/header; edit cart while unresolved and inspect the explicit choice; switch A/B and return to A; use operator to resolve Pending/Succeeded or late success after Cancel/expiry; observe refund Pending/Confirmed/NeedsReview from server. Open notifications, mark read and reconnect after offline work. Inspect storage to see only minimal scoped recovery (plus intentional shared cart/UI preferences). Do not delete recovery to “fix” an uncertain response. Current evidence is Chromium/loopback; Safari/Firefox, full WCAG/screen-reader audit, production TLS/affinity/shared keys, cross-device recovery and production load are not claimed.
