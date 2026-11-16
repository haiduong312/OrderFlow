# React frontend — session and cache foundations

Implemented pages: overview `/`, sign-in `/login`, registration `/register`, current user `/account`, and read-only Customer `/orders`. Design and cache/auth contract: [ADR-027](adr/0027-react-session-foundations.md). No catalog/cart/checkout/Admin/report/notification UI or production deployment is included.

## Run locally

Use Node **24.19.0** (`client/.nvmrc`) and npm **11.17.0**. All direct versions and npm lockfile are pinned; React 19.3.1, TypeScript 7.0.2, Vite 8.3.8, TanStack Query 5.107.1, Zustand 5.1.0 and Router 8.5.1 were checked against their stable npm metadata/peer engines. No .NET package/version changed.

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

Session/CSRF/passwords/private query data are never persisted. The only localStorage key is `orderflow-ui-v1`, containing version + `state.density`; unknown fields are ignored and the next write strips them. Drawer state is not persisted. Cookie tickets are HttpOnly; JavaScript receives only an in-memory antiforgery request token. Auth forms avoid putting passwords into a query/mutation cache. No cart/price/stock/payment-success state is manufactured.

The QueryClient is created once, with retry=false, staleTime=30s/gcTime=5min. Orders keys contain current user ID/page/pageSize/status. `/me` is a private zero-lifetime bootstrap key while the user ID is unknown. Private success/error results are generation-fenced; a late old-user 401 cannot log out the new account. BroadcastChannel, hidden/pagehide cleanup and focus/restore verification coordinate tabs; messages contain no private user data. There is no continuous cross-device revocation or instant remote-tab pixel-erasure claim. Server requests still authorize every operation. Owner checks on returned order DTOs also detect mismatched identity before caching rows.

Orders are read-only, show real API totals/status/expiry metadata and scoped pagination/status filters. Empty states require an actual successful empty page. Money remains JSON strings and uses bigint cents for exact formatting; dates display in the device's time zone with an explicit note. No cached frontend state can reserve stock, authorize a role or confirm payment.

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

The existing PostgreSQL/Testcontainers prerequisite applies. `FrontendBrowserTests` owns a freshly migrated DB and Kestrel API for each of nine scenarios, starts Vite/Chromium through Playwright, seeds generated users/roles/opening stock and uses real cookie/CSRF HTTP for orders. Node and matching browser binaries must be available. Missing prerequisites fail visibly, without skips/fake API responses. The normal API never maps the clock control; an extra service-free test checks GET/POST 404. The clock control exists only in the test host, bound to loopback and a random in-memory header key, and advances API TimeProvider by nine hours to exercise actual ticket expiry.

Default full `dotnet test` now includes these browser tests. For backend-only workflows use `--filter 'Category!=Browser'`; the service-free filter already excludes PostgreSQL (and therefore the browser cases). For a single case add `&DisplayName~account_switch_isolation` to the fully-qualified browser filter. `npm run test:browser` is the low-level driver; run it through the .NET harness, which supplies isolated backend/fixture environment variables. It intentionally cannot silently substitute a demo API when those are absent.

Scenarios: session restore; register/server field validation; invalid credentials/CSRF header tampering/fresh retry; missing and expired tickets; same-SPA account-switch/logout cache isolation; shared-cookie cross-tab switch/logout; deterministic pending request/real empty data/transport error/retry; unavailable `/me`; responsive keyboard/drawer/forms. Tests gate actual requests or abort transport; they never fulfill a made-up successful response. No test disables production auth/CSRF/rate limits. API hosts are isolated per case so the real per-IP quotas remain intact. Trace/video capture is off to avoid storing cookie/credential traffic; screenshots use generated example.invalid users only.

Screenshots/results are under `/tmp/orderflow-frontend/browser-artifacts` / `results` in the verified run; Playwright defaults to ignored `client/test-results` for low-level local execution. Desktop and 375px login screenshots were visually inspected; width checks also cover 320/768/1440px. The private app has no fixture seed/control route in production and no default login/password. No developer database was migrated/reset by implementation.

## Manual acceptance and limits

1. Register a new Customer with real backend roles provisioned. Try a 12+ character password missing required character classes; verify the server field error/code/trace. Successful creation leaves `/me` unauthorized until sign-in.
2. Sign in, reload `/account`, and inspect the HttpOnly auth cookie in browser tools. API traffic should stay under the Vite origin; local/session storage contains no token or user cache.
3. Open Orders, then Account → Switch account without reloading. A second account sees only its own rows. Sign out and use Back; protected content is gated. Repeat with two tabs sharing cookies.
4. Remove the auth cookie, then change an order filter to request data: 401 routes to sign-in. Browser tests separately prove genuine server ticket expiry via logical time. Tampered CSRF headers are rejected by the real API; retry retrieves a new pair.
5. Abort/block API traffic in browser tools: loading/error/retry is explicit, not a fake empty table or successful login. Missing/expired session and unavailable infrastructure are separate states.
6. At mobile width open navigation, tab within the drawer, press Escape and confirm focus returns to its button. Forms have labels/hints/errors, skip link/route heading focus and reduced-motion behavior. Compact view is the only persisted preference.

Verified Chromium/loopback development scope only. Safari/Firefox, a full WCAG/screen-reader audit, production TLS/proxy/CSP/shared keys, cross-device revocation, hostile suspended-tab scheduling, load testing and deployed SPA fallback remain unverified/deferred. No analytics, remote fonts, external media, fake sales metrics, catalog/cart/checkout or Admin/reporting operations are added. See [implementation status](implementation-status.md) for exact execution evidence.
