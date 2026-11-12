# Signed payment callbacks and atomic settlement

Current scope: roadmap 17 plus capture allocation/compensation intentions from 18. See [ADR-019](adr/0019-signed-payment-settlement.md), [provider setup](payments.md), [specification](project-spec.md) and [verification evidence](implementation-status.md). Refund execution/reconciliation now ship with [ADR-020](adr/0020-refund-execution-and-payment-reconciliation.md) / [recovery guide](payment-recovery.md); generic outbox dispatch remains pending.

## HTTP authentication and durable receipt

POST `/api/payments/simulator/callback` uses provider authentication only. No login or CSRF token is required; a cookie does not substitute for a signature. All browser mutations retain their global CSRF policy. Configure privately:

- API: `PaymentCallbacks__Enabled=true`, `PaymentCallbacks__KeyId` (default development-v1), `PaymentCallbacks__Secret` (32–512 characters).
- Simulator: `Simulator__CallbackKeyId` and `Simulator__CallbackSigningKey` must match those API values. Machine and control secrets remain separate.
- Worker: `PaymentSettlement__Enabled=true`; independent of optional `PaymentProcessing__Enabled`. Settlement has no gateway dependency and can run in its own process. Seed commands suppress all schedulers.

No actual secrets belong in tracked configuration or command output. Both new features default disabled; enabled API settings are validated at startup. A disabled callback returns 503 with a configured persistence service; no acceptance is claimed.

Send the simulator envelope's Body as the exact UTF-8 request bytes, with these headers:

| Header | Value |
| --- | --- |
| X-Payment-Key-Id | Envelope KeyId |
| X-Payment-Timestamp | Envelope Timestamp as canonical Unix seconds |
| X-Payment-Signature | Envelope Signature: 64 hex characters |

HMAC input is ASCII timestamp + `.` + exact raw body bytes. HMAC-SHA256 is compared with CryptographicOperations.FixedTimeEquals. Skew beyond five minutes is rejected (equal boundary allowed). Redelivery keeps event ID and body, with a fresh signature/timestamp. No reserialization, pretty printing, newline or whitespace change is allowed after signing. Encoded bodies are rejected. The schema requires exactly schemaVersion/provider/providerEventId/occurredAt/payment; payment is the existing gateway result with requestKey/attemptId/orderId/transactionId/amount/currency/state/captureId. Money is an invariant positive decimal string; state is exact Pending/Succeeded/Failed. Only Succeeded has a nonempty captureId. Duplicate/unknown/missing properties, invalid UUID/money/state/depth/JSON are rejected. A browser paid flag is not accepted.

| Result | Meaning |
| --- | --- |
| 202 | PaymentEvents and inbox committed, including exact duplicate; does not promise confirmation |
| 401 | Missing/forged/malformed credentials, changed raw bytes, stale/future signature, encoded body |
| 400 | Authenticated malformed/unsupported schema |
| 413 | Body over 64 KiB, including streaming/unknown length |
| 409 idempotency_conflict | Same authenticated provider event identity, different exact payload; alert 1601 |
| 503 | Disabled receipt or classified contention; redeliver original event with fresh signature |
| 500 | Unexpected failure; no fake acceptance |

Raw bytes and SHA-256 remain in PaymentEvents; the jsonb inbox is not used to reconstruct signature input. Receipt and inbox insertion commit together. Unsupported references/currency are durably accepted then quarantined with ErrorCode/Failed inbox and alert 1602; no arbitrary order attachment. Event/capture/refund IDs are separate identities.

## Business processing

Worker reads due Pending receipts/inboxes, opens a fresh scoped transaction, locks Order FOR UPDATE and samples normalized DecisionAt. It then binds transaction/capture identities, locks attempt/event/inbox and validates references against stored order/attempt. No network call occurs. Provider OccurredAt, HTTP ReceivedAt and business DecisionAt remain separate. A provider capture that occurred before expiry but is processed at/after expiry cannot allocate stock. An eligible DecisionAt stays authoritative if inventory later waits beyond the deadline.

| Verified fact / locked order | Atomic result |
| --- | --- |
| Exact USD capture, PendingPayment, DecisionAt < ExpiresAt | Captured attempt + allocated capture; Active reservations become Consumed; OnHand/Reserved decrease; Confirmed + System history; capture/confirmation outbox; processed inbox |
| Due PendingPayment (equality included) | Expired + release only Active reservations + System history/outbox; captured funds recorded; one Pending compensation |
| Cancelled/Expired/already paid or another winning capture | No resurrection/second consumption; capture + one Pending compensation |
| Known USD capture, wrong amount, before expiry | Capture actual amount + full Pending compensation; order remains pending, reservations unchanged; new eligible attempt may be created |
| Unknown/mismatched reference/transaction/capture, non-USD, non-capture amount mismatch | Quarantined authenticated observation + failed inbox; no confirmation or guessed refund |
| Duplicate capture under another event ID | No second capture/refund/consumption; overdue pending expiry can still finalize |
| Delayed failure/Pending after Captured | No regression of financial/fulfillment facts |

One allocated capture/order and one compensation/provider capture are protected by unique indexes. Deferred SQL constraints reject commits with unallocated captures lacking full matching compensation. Refund intentions are now Pending/Succeeded/NeedsReview and executed by the separate opt-in RefundWorker; see the recovery guide. Submission/lookup results never confirm orders; callbacks clear its lease token, fencing late responses. Browser initiation replay still returns original 202 bytes; GET attempt/order supplies current state.

Settlement polls up to 20 sequential events per pass, five seconds between passes, fresh scope per event. This local database consumer keeps effects and inbox completion in one transaction without a network lease. Known SQL contention rolls back first, schedules a durable retry five seconds later, and stops automatic retries after eight failures with a Failed inbox. Unknown errors propagate/stop the host visibly. Inspect PaymentEvents.Status/ErrorCode and ProcessedMessages.Status/AttemptCount/LastErrorCode for backlog/failure; audited replay tooling is deferred. Logs contain IDs/codes, never secrets/signatures/raw bodies.

## Manual verification

Apply the additive migration explicitly after private database configuration; startup never migrates:

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet tool restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
```

Migration: **20261111040724_AddSignedPaymentSettlement**. It creates PaymentEvents/PaymentCaptures/Refunds, adds ConsumedAt and extends existing state/audit constraints. Existing evidence is retained; Down refuses to discard payment facts.

1. Follow [payments](payments.md) to start merchant API, restricted simulator, and submission Worker with matching private credentials. Enable callback receipt and settlement as described above.
2. Log in as Customer, checkout, initiate payment with CSRF/client key; configure simulator Succeeded (or resolve an existing Pending payment), let submission finish.
3. Obtain a fresh envelope from the operator-only POST `/control/payments/{providerRequestKey}/callback`. The simulator generates it but does not send it. Save the envelope privately to `/tmp/orderflow-callback-envelope.json`.
4. Send its exact Body using Python below, with MERCHANT_ORIGIN privately set to the local API origin. It needs no merchant signing secret because the simulator already signed the envelope:

```bash
python3 - <<'PY'
import json, os, urllib.request
with open('/tmp/orderflow-callback-envelope.json', encoding='utf-8') as f:
    e = json.load(f)
request = urllib.request.Request(
    os.environ['MERCHANT_ORIGIN'].rstrip('/') + '/api/payments/simulator/callback',
    data=e['body'].encode('utf-8'), method='POST',
    headers={'Content-Type': 'application/json', 'X-Payment-Key-Id': e['keyId'],
             'X-Payment-Timestamp': str(e['timestamp']), 'X-Payment-Signature': e['signature']})
with urllib.request.urlopen(request) as response:
    print(response.status)
PY
```

5. GET owner order/attempt: eventually Confirmed/Captured with one consumption per reservation. Redeliver the envelope within its signature window: 202, same capture and no extra stock effect. Beyond five minutes obtain a refreshed envelope.
6. For late capture, cancel first or let expiry finalize, then deliver a signed success: order stays terminal, OnHand stays unchanged by payment, Reserved is released once, and PaymentCaptures has matching Pending compensation. With RefundProcessing enabled, the simulator processes the full compensation by its original key; no real money provider is used.
7. Edit Body without a fresh signature: 401. To test authenticated amount mismatch use simulator MismatchedAmount; it cannot confirm and creates full compensation for the observed USD amount.

Focused automated checks (Docker/Testcontainers or private actual ORDERFLOW_TEST_POSTGRES required for PostgreSQL):

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~PaymentCallbackTests|FullyQualifiedName~PaymentSettlementDomainTests'
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

Race gates count independent contexts before their contested Order SQL or receipt INSERT. One-way lock arrival/pause signals plus logical time test deadlines; no real sleeps prove concurrency. Fresh-context assertions reconcile ledger/balances/reservations/captures/refunds. Fault injection verifies receipt/inbox atomicity and rollback after actual consumption SQL when mandatory outbox fails. SQL tests cover immutable evidence, terminal disposition and missing compensation.

Limits: no scheduled simulator callback delivery, general outbox dispatcher, operator replay UI, real provider or production-readiness claim. Durable backlog is safe but delays allocation; hosts require synchronized UTC. No exactly-once external side-effect guarantee.

ADR-020 extends evidence provenance: authenticated gateway lookup now shares PaymentSettlement; captures first observed by lookup have Source=ProviderLookup and null provider event/occurrence, while a later callback remains a separate authenticated receipt without creating another capture/refund. The signature protocol is unchanged.
