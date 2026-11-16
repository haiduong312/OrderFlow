# OrderFlow client

React/TypeScript session/catalog/cart/checkout/payment/notification/Admin UI, stable TanStack Query cache and bounded Zustand cart/UI preferences. Start here: [frontend setup, privacy contract and browser tests](../docs/frontend.md), [ADR-027](../docs/adr/0027-react-session-foundations.md), [cart contract ADR-028](../docs/adr/0028-catalog-cart.md), [recovery/live contract ADR-029](../docs/adr/0029-checkout-recovery-and-live-ui.md), [Admin runbook](../docs/admin-ui.md) and [ADR-030](../docs/adr/0030-admin-workspace.md).

Use Node 24.19.0 / npm 11.17.0. Run `npm ci`, then `npm run dev`. The same-origin `/api` development proxy targets `http://127.0.0.1:5080`; configure/run the existing API with its private DB, migrations, roles and explicit Development HTTP opt-in first. No fake API or default credentials are supplied.

`npm run build` type-checks and produces dist; `npm run test` checks fetch/session/catalog/cart/money/query/Admin recovery contracts; `npm run format:check` verifies readable source. Real Playwright tests run through `FrontendBrowserTests` and `CheckoutBrowserTests` in the .NET PostgreSQL harness; see the runbook. A production build is not deployment/proxy configuration.

Optional live updates: VITE_ORDERFLOW_REALTIME=true with the existing backend Realtime/Redis configuration. The official SignalR 10.0.11 client uses cookie/CSRF negotiation and ws-enabled same-origin Vite proxy. No simulator secret or customer payment redirect is supplied. Recovery storage is per authenticated user and must be retained on uncertain results; follow the runbook before discarding an intention.
