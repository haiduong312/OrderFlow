# ADR-018: Durable payment initiation and a restricted separate simulator host

Date: 2026-11-06. Status: Accepted. Extends ADR-003/004/005/009/017. Supersedes the co-hosted simulator detail of ADR-005/architecture; retains the modular monolith's production projects and one merchant transaction boundary.

## Scope clarification

Latest request prefers a small independent simulator host, overriding the earlier worker-adapter-only choice. Add one optional Development/Testing project, not a public production success endpoint. The request says callback settlement is deferred to milestone 13, but repository 13 already delivered order management; existing roadmap assigns receipt/settlement to 17/18. Preserve stable roadmap IDs and implement only signed callback generation now. Requested scope delivers 15 plus provider/submission/reconciliation foundations of 16. Remaining messaging 14 is not claimed complete; durable attempts/expiry/identity are sufficient prerequisites for this queue.

## Decision

Customer payment initiation accepts only an empty body, cookie/CSRF and client key; acquires the owner Order lock; derives amount from immutable order total; inserts/reuses an unresolved attempt and commits original 202 response atomically. Scope client keys by user/order/operation. Same-key replay preserves original bytes, and a different key links to the active attempt. A partial unique index allows only one Queued/Pending/Unknown attempt per order. New attempts after definitive failure need a new client key and unexpired PendingPayment. Overdue new requests commit safe expiry/release and then return the documented conflict.

Submission is asynchronous. Callable PaymentProcessingIteration claims one due attempt with a token/60-second lease, commits/disposes its scope, invokes IPaymentGateway outside any merchant transaction, then uses a fresh scope and token-fenced observation transaction. Queued submissions and uncertain reconciliation retain the original provider key. Pending/Unknown lookup before resubmitting an absent provider record. Eight deliveries maximum, bounded due delays and visible NeedsAttention; no timeout-to-Failed conversion. Optional sequential Worker scheduling is explicit and seed/test controllable.

Do not promote submission/lookup success to confirmed payment. Retain matching provider transaction/capture observations as Pending + settlement_pending, blocking new attempts. Mismatch is Unknown/NeedsAttention with retained observation. Only provider-definitive valid failure permits replacement. No capture/order/inventory settlement or merchant refund tables/UI here.

Simulator HTTP state uses its own configured database connection and transactions; no merchant FKs. Stable provider request/transaction/capture/refund/event IDs and PostgreSQL advisory locks/unique constraints protect repeated/concurrent calls. Controls default Pending, support success/failure/timeout before accept/timeout after committed capture/mismatched amount, and preserve terminal captured evidence. Provider full refunds are idempotent; merchant compensation remains later.

Require explicit enablement in Development/Testing and loopback binding; forbid Production/Staging. Separate machine/control/HMAC secrets; Customer cookies cannot authorize any provider/control action. No simulator route is registered in the API. Callback generation returns stable raw body/event ID and a fresh timestamp HMAC; no unsigned endpoint, callback delivery or settlement shortcut. Tests use actual Kestrel HTTP and a second PostgreSQL database to demonstrate persistence independent of merchant network outcomes. The shared migration chain remains a deliberate small-project trade-off.

## Verification / consequences

Actual PostgreSQL tests cover client/DB idempotency, eligibility/ownership/CSRF, original response replay, definitive failure replacement, timeout/restart, quarantined mismatch, lease fencing/lost observations, provider idempotency/refunds and signed callback redelivery. Independent NOWAIT merchant locks at gateway invocation prove transaction separation. No in-memory provider dictionary or EF InMemory fallback. Additional operational costs: optional host/secrets, polling lag, unresolved outcomes held for settlement/reconciliation; no exactly-once external-effect claim.

See [payment runbook](../payments.md), [status](../implementation-status.md), [HttpClient guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines) and [PostgreSQL partial uniqueness](https://www.postgresql.org/docs/18/indexes-partial.html).
