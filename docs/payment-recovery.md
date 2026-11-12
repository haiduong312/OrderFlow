# Refund execution and payment reconciliation

This completes late-capture compensation delivery with the existing IPaymentGateway. It does not add general customer refunds or returns. See [ADR-020](adr/0020-refund-execution-and-payment-reconciliation.md), [callback settlement](payment-callbacks.md), [simulator setup](payments.md), and [actual verification evidence](implementation-status.md).

## Configuration and migration

No packages/SDK versions changed. Apply **20261111044314_AddRefundRecovery** explicitly after private database configuration; hosts never migrate automatically:

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet tool restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
```

The forward migration preserves each prior RefundPending identity/key/amount/capture and initializes Pending, UpdatedAt and NextAttemptAt from CreatedAt. It adds delivery/lease/result metadata, due/result indexes, terminal/snapshot guards, capture evidence provenance, optional provider event/time for lookup captures, and simulator refund scenarios. Coordinate the migration with the new API/Worker binaries: stored status names and nullable lookup provenance extend the previous model. Downgrade refuses loss of refund execution evidence. For a separate provider DB, explicitly apply the same migration chain there too. No runtime schema migration or developer data reset occurs.

Enable separately on the Worker:

```text
PaymentProcessing__Enabled=true
PaymentReconciliation__Enabled=true
RefundProcessing__Enabled=true
PaymentSettlement__Enabled=true
```

All flags default false except normal expiry. PaymentSettlement consumes received signed callbacks; PaymentReconciliation performs status lookups and can settle even without a callback. RefundProcessing executes compensation intentions. Submission/reconciliation/refund workers share the configured IPaymentGateway: private PaymentGateway__ApiKey matching Simulator__ApiKey and loopback PaymentGateway__BaseUrl. Simulator transport is restricted to Development/Testing; callback credentials remain separate. Seed mode suppresses all loops. Each loop processes at most 20 sequential items/pass, waits five seconds with shutdown cancellation, and owns fresh database scopes per phase.

## Refund state and recovery flow

| State | Durable meaning |
| --- | --- |
| Pending | Unallocated capture needs full compensation; may include uncertain prior delivery and a future NextAttemptAt |
| Succeeded | A correlated provider refund ID/result is committed locally, with one refund-succeeded outbox event |
| NeedsReview | Rejected/mismatched/exhausted work is stopped visibly; it does not prove no external refund occurred |

No Unknown/Failed refund enum is necessary for this requested model; uncertainty stays Pending with LastErrorCode, permanent/exhausted uncertainty is NeedsReview. Provider key, capture, amount/currency and FullCompensation purpose are immutable. Successful and completed-review facts cannot automatically regress.

1. Short PostgreSQL claim transaction selects one due Pending refund, FOR UPDATE SKIP LOCKED, issues a fresh token/60-second lease and increments DeliveryCount; commit/dispose.
2. Outside all merchant transactions, LookupRefundAsync(original provider key). If absent, RefundAsync with the same key/capture/full actual amount/USD. No unrelated identity is issued.
3. Fresh scoped transaction locks refund, checks the current token/terminal state, validates returned request key/capture/amount/currency/refund ID, saves Succeeded + outbox or durable retry/review, commits.

Timeout after provider commit is uncertain locally: the next due worker finds the same provider refund via lookup. A crash before local acknowledgement leaves a lease; a new worker reclaims after exact expiry and queries the original key. Stale workers cannot overwrite a newer owner or success. An independent NOWAIT connection in tests proves the committed refund is not locked by a merchant transaction at both network operations.

Backoff per committed delivery is 5,15,30,60,120,300,600,600 seconds. Maximum eight deliveries; the eighth claim itself marks review before HTTP so a crash cannot leave hidden exhaustion. The current last token may still finish successfully. HTTP 408/429/5xx and transport uncertainty are retryable; other HTTP rejection or result mismatch requires review. Unknown database/application failures propagate visibly. A failed claim before commit makes no external call; after a committed claim any retry must recover the same key.

## Payment lookup and shared settlement

The reconciliation worker claims previously submitted/recoverable Queued, Pending and Unknown attempts with the existing shared lease/counter; it also handles settlement_pending and gateway_response_mismatch observations. Original submission can recover absence under its original key; this new worker is lookup-only. It never turns 404 into Failed or resubmits a new payment. Only one submission/reconciliation token can own the row at once.

After lookup outside the transaction, lock the order from the persisted intention, sample DecisionAt, lock provider identities and the persisted attempt, recheck token, then invoke **the same PaymentSettlement** used by callbacks. Provider-supplied IDs cannot select another order/attempt for processing. Shape, stored order/attempt association, request key, transaction/capture, USD and authoritative amount all remain validated.

| Lookup | Result |
| --- | --- |
| Valid Pending | Keep Pending, release lease and schedule bounded backoff |
| Valid definitive Failed | Fail attempt; leave order/stock unchanged; eligible client can start replacement |
| Matching exact USD capture before expiry | Same atomic capture/consume/confirm/history/outbox as callback |
| Capture after expiry/cancel or surplus | Record capture, expire/release if due, one full-compensation intention; never resurrect |
| Known USD capture, wrong amount | Record actual captured amount and full compensation; no confirmation |
| Wrong IDs/key/transaction/currency or malformed result | Preserve bounded LastObservation when available; NeedsAttention/manual review, no allocation |
| Missing/uncertain lookup | Unknown/backoff; maximum shared eight deliveries then NeedsAttention with reconciliation_exhausted |

Callback clears the attempt's lease, so old lookup Pending/Failed cannot regress verified capture. Conversely, a callback after lookup settlement sees the same capture and adds no financial/stock/refund effect. Captures from lookup have Source=ProviderLookup and null ProviderEventId/ProviderOccurredAt: this gateway contract supplies neither. RecordedAt and order-lock DecisionAt are merchant times, not invented provider occurrence. Callback receipts retain their original provider event/time and authentication path.

## Simulator behavior and manual checks

Start the restricted simulator and merchant API/Worker using private configuration from [payments](payments.md). The simulator must use an independent configured database/transaction boundary. Operator-only controls use X-Simulator-Control-Key; merchant API credentials or Customer cookies cannot authorize them.

Configure a per-refund key scenario before the provider refund exists:

```text
PUT /control/refunds/{providerRefundKey}/scenario
{"scenario":"TimeoutAfterSuccess"}
```

Supported scenarios: Succeeded(default), TimeoutBeforeAccept, TimeoutAfterSuccess, Rejected. TimeoutAfterSuccess commits provider refund first then withholds the response until caller cancellation; lookup and same-key retry recover it. Rejected returns 422 without refunding. Existing committed effects always take precedence over scenario changes; controls cannot rewrite/refund twice. No arbitrary real delay is used as a concurrency proof.

Manual sequence:

1. Checkout and initiate payment. Configure simulator Succeeded or TimeoutAfterCapture; submit using the normal Worker.
2. Cancel before receiving/processing capture, or let the order reach its deadline. Enable reconciliation; inspect order remains Cancelled/Expired and one PaymentCapture + Pending refund appears.
3. Set TimeoutAfterSuccess for that immutable ProviderRefundKey, enable RefundProcessing. Inspect provider ProviderRefunds: one row; local refund Pending + gateway_timeout and NextAttemptAt.
4. Restart Worker/provider if desired, retain both databases. Once due/lease expired, lookup stores the same provider refund ID locally as Succeeded. No second full refund or stock release occurs.
5. Redeliver a signed late callback: 202 and no second capture/refund/stock effect. GET owner order remains terminal.
6. For Pending provider payment, resolve via operator control to Failed or Succeeded and let lookup run. Failed leaves stock reserved until normal cancellation/expiry; Succeeded follows normal eligibility rules.
7. For Rejected or exhausted transport failures inspect NeedsReview / LastErrorCode / DeliveryCount. Investigate original ProviderRefundKey and capture with provider lookup; do not create a new key. Audited replay tooling remains future work.

Useful read-only SQL (private connection; do not print raw observations/secrets):

```sql
SELECT "Id","CaptureId","Status","DeliveryCount","NextAttemptAt","LeaseExpiresAt","ProviderRefundId","LastErrorCode"
FROM payments."Refunds" ORDER BY "CreatedAt";
SELECT "Id","OrderId","Status","DeliveryCount","NeedsAttention","LastErrorCode","ReconciledAt"
FROM payments."PaymentAttempts" ORDER BY "CreatedAt";
```

Tests:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~PaymentRecoveryTests|FullyQualifiedName~RefundDomainTests'
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

PostgreSQL tests require Docker/Testcontainers or privately configured actual ORDERFLOW_TEST_POSTGRES. They use independent merchant/provider databases and real loopback HTTP, logical business time, separate contexts, deterministic pre-claim/order-lock barriers, and acknowledgement fault injection. One-second HTTP timeouts intentionally model transport uncertainty; there are no sleeps claiming race correctness.

## Provider-dependent limits

The simulator guarantees durable immutable request-key binding, stable transaction/capture/refund IDs, full-refund uniqueness by capture/key, and commit-before-response with authoritative lookup after restart. It retains identities indefinitely. An actual provider must offer equivalent idempotency/lookup retention within the merchant account; without them, a local lease cannot prevent duplicate external effects after an uncertain result. Tests prove one effective refund per simulator capture, not universal external exactly-once delivery.

Only definitive provider failure should become Failed; if failure is provisional, map it to Unknown. Simulator Failed->Succeeded is an explicit fault scenario requiring a later verified callback after Failed is terminal; this worker does not poll definitive failures forever. Real integrations need reliable later notifications or audited same-identity recovery. Automatic callback delivery, operator replay tooling, general outbox publishing, real-provider integration and production deployment remain deferred. No customer refunds/returns were added.
