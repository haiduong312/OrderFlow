# ADR-030: Admin workspace, authoritative mutations and bounded recovery

- Status: Accepted
- Date: 2026-11-16
- Scope: milestone 22, report/audit UI of 24 and the existing email-failure operation of 28

## Context and supported scope

The existing Admin APIs already implement catalog version checks, durable stock operation identities, fulfillment transitions, bounded direct PostgreSQL reports/audit, and audited email retries. Extend the existing React/Vite workspace and exact installed dependencies. No package, schema migration, production backend write algorithm, event projection, generic job executor or new retry endpoint is needed.

Order search supports exact order UUID navigation and paginated status/customer UUID filters. Do not invent unsupported email/keyword search. The fulfillment graph is Confirmed → Processing → Shipped → Completed. PendingPayment can become Confirmed only through verified server settlement. Customer cancellation and expiry retain their existing contracts; Admin UI cannot force payment, skip states or revive terminal orders.

Only email has a global failure-list/retry API. Display paginated email failure metadata and the existing per-order payment/capture/refund audit. Global failed-job/outbox/refund lists/retries remain absent. No raw payload, recipient/body, gateway/provider key, SQL or arbitrary execution form is rendered.

## Reads, authorization and invalidation

All Admin queries use `['private', verifiedUserId, 'admin', resource, { every API parameter }]`, staleTime zero, AbortSignal and the existing session generation guard. The route gate and navigation use server-confirmed roles for UX; every API still independently enforces Admin policy. Existing Identity cookies, typed ProblemDetails/trace/code handling and fresh antiforgery headers apply. Logout/account change purges private query/mutation data before another identity can render it. Admin recovery contexts are also recreated per verified user.

Use TanStack mutations with retry disabled. Await targeted invalidation after settlement, including uncertain failures: catalog changes refresh affected public details/lists and Admin catalog/low-stock reads; stock also refreshes that product's balance/history and stock audit; fulfillment refreshes the order/list/history/status counts; email refreshes the failure page. Do not replace monetary/stock/order state optimistically. Cancel/restart affected older reads through normal Query invalidation. Unrelated users/resources remain untouched.

Product/category editors wait for a fresh read instead of exposing a cached form that can be replaced during editing. An actual concurrent writer can still win after the read; expectedVersion/SQL checks reject the stale write and the workspace retains the conflict code/trace even when the editor remounts. Snapshots in separate requests can differ; stock availability is a read, never authorization for an adjustment or checkout. Admin pages are not continuously polled: refresh/focus/remount or a mutation triggers reads. A tab left untouched can age indefinitely. Public catalog retains ADR-025's bounded lookup staleness; no stronger cross-node guarantee is claimed by frontend invalidation.

## Durable, narrowly allowlisted browser commands

Persist one versioned command under `orderflow-admin-command:v1:<verified-user-UUID>` before sending it. The record contains only allowlisted resource/method and its original primitive body, at most 12 KiB UTF-8 on write/read. It may include catalog fields or a required audit reason; never auth tokens, private response caches, provider payloads or secrets. Browser storage is not encrypted against same-origin script access. Retain unresolved data across logout/reload so the same Admin can resolve it; another account's UI does not load it. The device cart retains ADR-028's independent shared-browser policy.

Native per-user Web Locks serialize same-origin tabs and re-read storage inside the lock; an instance also synchronously rejects a second concurrent submit before disabled controls render. Unsupported/unavailable storage/locks, malformed/future/oversized records or disappearance of an unresolved record fail closed. There is no silent memory-only fallback or automatic TTL/rekey. Independent devices or separately authorized intentions can still issue different operations; PostgreSQL remains the correctness boundary.

A stock receipt/adjustment uses one UUID operationId and exact quantity/delta/reason. An email retry uses one UUID operationId and exact delivery/reason/duplicate-risk acknowledgement. Transport loss, timeout, 5xx or unconfirmed acknowledgement retains that body/identity and blocks new writes. Deliberate recovery replays the original body to the original endpoint. The server returns the existing immutable movement or audited retry, rather than applying another effect. A stock replay result is historical; the balance is fetched separately.

Catalog/fulfillment commands have no general replay contract. Do not automatically replay them after uncertainty, particularly category creation because category names are not unique. Show submitted name/SKU/version/resource for inspection. The Admin must inspect server state/history, then explicitly acknowledge closing local recovery before submitting a new intention. Closing does not undo a committed effect. A definite first 400/403/404/409/422 means no write and permits correction; after prior uncertainty even a rejection retains the original record for deliberate resolution. Lost browser data across reload cannot reconstruct an old identity.

The existing email retry endpoint remains Admin + CSRF protected and audited, with a database lock/state check and unique operation identity. It accepts only NeedsReview, requires acknowledgement for uncertain provider outcomes, preserves delivery/provider identity and lifetime AttemptCount, and advances the audited retry cycle once. Replaying that same operation after Pending/Sent returns metadata without another schedule. A new operation against Pending/Sent is rejected. SMTP uncertainty can still duplicate accepted mail; this does not promise exactly-once external delivery. There is no payment/refund retry button.

## Financial and audit semantics

Preserve ADR-026's direct report semantics: paid sales are winning confirmed-order immutable item values at confirmation time; captured revenue includes allocated and late/surplus verified captures at recorded time; refunds count only Succeeded at succeeded time; net equals captures minus succeeded refunds and can be negative. Merely created or Completed orders do not prove revenue. Current status counts filter order creation time. Top products use paid historical item snapshots. Low available stock uses active catalog and OnHand − Reserved independently of report dates.

Money stays invariant decimal strings; formatting supports signed report aggregates without conversion through JavaScript Number. Date filters are UTC `[from,to)`, at most 366 days. Timestamp display is device-local and labeled; UTC midnight is 07:00 in Vietnam, so these are not Vietnam calendar-day filters. Separate report/audit results expose their server ObservedAt. There is no additional projection/checkpoint/replay engine.

Audit views whitelist safe DTO fields, paginate and are read-only. No edit controls or write endpoint are added. Fulfillment/stock reasons enter their existing immutable histories; email retry uses its existing audit row. Catalog mutations retain existing version checks; a new catalog audit platform is outside this milestone.

## Verification and consequences

Vitest covers parameter/scope keys, real Query cancellation, targeted invalidation, allowed transitions, exact signed money, UTC/int bounds and fail-closed recovery/replay/duplicate-submit behavior. Six browser cases use normal Identity/CSRF, isolated PostgreSQL/Redis/Kestrel/Vite/Chromium and the independent payment simulator. Response loss first commits a real API response, then drops transport. Deterministic route/response gates establish readiness without race sleeps. A test-only driver prepares coherent email NeedsReview metadata; it is never mapped by the normal API.

See [status](../implementation-status.md) for exact checks/failures/results and [Admin runbook](../admin-ui.md) for manual acceptance. Full WCAG/screen-reader, Safari/Firefox, deployed HTTPS/proxy/shared keys, cross-device recovery, production load and generic backlog operations remain unverified/deferred.

TanStack's official [mutation lifecycle](https://tanstack.com/query/latest/docs/framework/react/guides/mutations) and [invalidation guide](https://tanstack.com/query/latest/docs/framework/react/guides/invalidations-from-mutations) support the installed v5 lifecycle: put essential feedback/closure in hook callbacks, since per-call callbacks can disappear on unmount, and return the invalidation promise to keep mutation pending through refresh.
