# Payment initiation and isolated simulator — milestones 15 / submission slice of 16

This milestone implements durable payment intentions, provider submission/lookup and a separately runnable development/test simulator. It does **not** implement callback receipt, capture settlement, stock consumption, merchant refund workflows or a browser success redirect. Provider success leaves the merchant attempt unresolved and the order PendingPayment until future verified settlement. See [ADR-018](adr/0018-payment-initiation-and-simulator.md), [architecture](architecture.md), [expiry](expiry-worker.md) and [implementation status](implementation-status.md).

## Browser contract

| Endpoint | Contract |
| --- | --- |
| POST `/api/orders/{orderId}/payment-attempts` | Owning Customer + real cookie/CSRF, exactly one Idempotency-Key, empty body or `{}`; 202 persisted attempt snapshot + Location, no-store |
| GET `/api/orders/{orderId}/payment-attempts/{attemptId}` | Owning Customer; current attempt/status/amount/observed provider state/NeedsAttention/error code, no-store; no gateway call or mutation |

Reject undeclared JSON, including amount/currency/ownership/status/provider scenario. Amount is the immutable order total (numeric(18,2)); public money is an invariant decimal string, USD. Non-owner/missing resource is 404, unauthenticated 401, Admin-only identity 403, missing CSRF 400, invalid key 400. An Admin is not implicitly a Customer. No Customer role/field can select simulator outcomes.

Same-key replay returns the original accepted response bytes/status/Location, including after provider failure or order progression. It never starts another attempt or calls a gateway. GET supplies current state. Ownership is rechecked on every replay. Namespace is `(userId, StartPayment:v1/{orderId:D}, key)`; its canonical request is `{"version":1}` and the shared store retains the SHA-256 hash. Keys may be reused on a different order because that is a separate namespace. A fresh key while any Queued/Pending/Unknown attempt exists links to that same attempt. Only a definitively Failed attempt permits a replacement with a new client key, while the order is still eligible.

## Initiation transaction

1. Validate authentication/key/body. Begin PostgreSQL Read Committed transaction, local 5s lock/20s statement limits.
2. Acquire the owner-constrained Order FOR UPDATE. All initiation requests for this order serialize before checking client keys/active attempts. No process-local lock or check-then-insert arbitrates this race.
3. Replay a committed existing client key if present. For a new key, sample injected normalized UTC. PendingPayment at exactly ExpiresAt is overdue: expire/release through the shared order-before-inventory logic, append System history/outbox, commit, then return 409 order_expired. Other nonpending states conflict. Replays do not create new work or extend the deadline.
4. Reuse the unresolved attempt or insert Queued PaymentAttempt with server order amount/currency and stable independent provider request key `payment-{attemptId:N}`. This key is not the checkout key, client request key, provider transaction ID, capture ID or event ID.
5. Save the original accepted response in IdempotencyRecords and commit the attempt/key atomically. No gateway service is registered in this HTTP use case. Transaction failure leaves neither a new attempt nor a consumed client key.

The partial unique `UX_PaymentAttempts_ActiveOrder` on OrderId for Queued/Pending/Unknown is the database backstop. Supported writers all acquire the Order lock, so concurrent initiation avoids a unique exception in normal operation. A storage constraint violation is not recovered by querying inside an aborted transaction; errors propagate and roll back. Duplicate identity recovery is through a fresh HTTP retry after rollback. Payment processing never creates an attempt or changes the idempotent response.

## Submission/reconciliation outside the transaction

`PaymentProcessingIteration.ProcessNextAsync` separates three phases:

1. Fresh scope/context: claim at most one due Queued/Pending/Unknown attempt with FOR UPDATE SKIP LOCKED, a fresh lease token, 60s lease and incremented DeliveryCount. Commit and dispose the scope **before HTTP**. This claim only touches attempts; it never takes an order/inventory lock after an attempt lock.
2. Outside any merchant transaction: submit a Queued attempt; for Pending/Unknown, first lookup by its original provider key, then submit with that same key only if the provider reports no record. Default external timeout is 15s, shorter than the lease. No automatic HttpClient retry/redirect is enabled. Cancellation propagates.
3. Fresh scope/context/transaction: lock the attempt, verify the current lease token and persist the observation. A stale owner cannot overwrite a reclaimed lease's result. No order/inventory mutation or settlement occurs here.

`IPaymentGateway` exposes Submit, Lookup, provider Refund and LookupRefund. `SimulatorPaymentGateway` uses bounded responses, machine credentials and a pooled HttpClient. Timeout/network/non-success/malformed response is uncertain, not a definitive payment failure. Known gateway uncertainty is recorded as Unknown with a sanitized code and warning; SQL/unknown application failures propagate rather than become successful payment results. A shutdown/crash after claim leaves its lease for restart recovery.

| Observation | Merchant result |
| --- | --- |
| Valid Pending | Pending; lookup scheduled |
| Valid Failed | Failed; a new client key may create a replacement before expiry |
| Valid Succeeded | Pending + observed Succeeded + NeedsAttention + settlement_pending; blocks replacement; order stays PendingPayment |
| Timeout/network/protocol uncertainty | Unknown, original identity retained, reconciliation scheduled |
| Wrong amount/currency/attempt/order/key/transaction identity or invalid capture shape | Unknown + NeedsAttention + gateway_response_mismatch; retain observation for investigation; block replacement |

Retry delays after delivery are 5s, 15s, 30s, 1m, 2m, 5m, 10m; maximum eight claimed deliveries. Exhaustion retains unresolved state with NeedsAttention; it never frees the active slot by guessing failure. NeedsAttention and definitive failure are excluded from automatic claims. Operator replay tooling is a later milestone; no unsafe retry/status-setting endpoint is added here. Queued intentions survive a Worker restart. A provider effect with a lost merchant observation is rediscovered by the stable key after lease expiry.

`PaymentWorker` is opt-in (`PaymentProcessing__Enabled=false` by default). Enabled mode schedules at most 20 sequential attempts/pass with a cancellable TimeProvider-aware 5-second delay. Each processor phase owns its DI scope. Known PostgreSQL contention logs 1503 and postpones the pass; unexpected failures stop Generic Host visibly. Gateway uncertainty logs 1502, NeedsAttention 1504; IDs/codes are logged, not payloads, secrets or signatures. Seed commands suppress jobs.

Admission is decided under the order lock; provider I/O happens later and can complete after cancellation/expiry. Provider capture evidence is retained, but this milestone cannot allocate it or refund it. Future signed settlement must apply the existing deadline/compensation rules under order-before-inventory locks; it must never treat submit/lookup/browser results as confirmed order payment. Use this simulator only for development/test, not real funds.

## Separately controlled provider host

`OrderFlow.Simulator` is an optional development/test host. Production/Staging startup is forbidden, explicit Simulator:Enabled=true is required, and all configured listening URLs must be loopback; alternate Kestrel endpoint configuration is rejected. It has no browser auth, no CORS and no unauthenticated control route. The API does not discover simulator routes.

Three independent private secrets of 32–512 characters are required: Simulator:ApiKey, Simulator:ControlKey and Simulator:CallbackSigningKey. Generate them securely outside Git; do not print, commit or embed them in URLs. Control access compares credentials in fixed time. The API key cannot authorize controls or callback generation. Tests generate secrets per host and never use real credentials.

| Route | Credential / behavior |
| --- | --- |
| POST `/provider/payments` | X-Simulator-Key; request key/attempt ID/order ID/amount/currency from merchant; stable transaction/capture under retries |
| GET `/provider/payments/{key}` | X-Simulator-Key; persisted provider result or 404 |
| POST `/provider/refunds` | X-Simulator-Key; full captured USD amount and separate refund key; idempotent same key/body, conflict on changes or a second full refund identity |
| GET `/provider/refunds/{key}` | X-Simulator-Key; persisted refund ID/result or 404 |
| PUT `/control/payments/{key}/scenario` | X-Simulator-Control-Key; `{ "scenario": "Succeeded" }` before payment exists |
| POST `/control/payments/{key}/resolve` | X-Simulator-Control-Key; resolve Pending to Succeeded/Failed; captured success cannot regress; a later capture may supersede an earlier failure |
| POST `/control/payments/{key}/callback` | X-Simulator-Control-Key; generate signed callback envelope only, no API settlement/delivery |

Scenario values: Pending (default), Succeeded, Failed, TimeoutBeforeAccept, TimeoutAfterCapture, MismatchedAmount. No arbitrary real delay is needed for scenario correctness: timeout controls intentionally withhold a response until the HTTP caller cancels. TimeoutAfterCapture commits the provider capture before withholding it; lookup still works. TimeoutBeforeAccept creates no provider payment; changing its operator scenario allows later submission under the original key. Controls cannot rewrite an already created payment's request facts/amount.

Provider state lives in `simulator.ProviderPayments`, ProviderScenarios and ProviderRefunds; there is no FK to merchant orders/attempts. Transaction/capture/refund/event identities are persisted. Submission obtains PostgreSQL transaction advisory locks in attempt-ID then provider-key order, checks immutable request facts, and commits independently. Refund locks serialize capture then refund key. Unique constraints and SQL snapshot/capture/refund guards reinforce identities across hosts/restarts; no in-memory dictionary pretends to be provider durability.

The host accepts its own ConnectionStrings__OrderFlow. It may use another database/server; the tests use a second isolated migrated database and real loopback Kestrel HTTP. The shared migration chain creates both schemas for simplicity, so a standalone provider database also contains unused merchant tables. No provider code accesses them. A separate provider-only migration project/production runtime roles remain later hardening, not another domain architecture layer.

## Signed callback generation and refunds

Callback envelope fields: KeyId, Timestamp (Unix seconds), Signature (hex HMAC-SHA256), Body (the exact JSON text). Sign bytes are UTF-8 `timestamp + "." + Body`. Body contains schemaVersion=1, provider=Simulator, stable providerEventId/occurredAt and payment result. Redelivery preserves event ID/body and gets a fresh signed delivery timestamp. CallbackSigningKey is distinct from the machine/control keys; CallbackKeyId defaults to development-v1. Generation does not send an HTTP callback and no API callback endpoint exists yet. Future receipt must verify raw bytes/timestamp skew/fixed-time signature before durable inbox acceptance; future settlement owns order/capture/refund effects.

Refund methods implement the provider contract now: one stable full-refund result per capture, repeated key/body returns the same RefundId; changed amount/capture or another full refund identity conflicts. There are no merchant Refunds tables, refund worker or customer paid-order cancellation/refund UX. Provider refund success is not evidence that a merchant compensation workflow has been implemented.

## Setup and manual verification

After private merchant DB configuration, run:

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet tool restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
```

This explicitly applies **20261106084012_AddPaymentInitiationAndSimulator**. For a separate provider database, run the same migration command with that database's private connection in a separate terminal. Hosts never migrate automatically. The additive migration creates four tables, bounds/uniqueness/FKs/claim indexes and immutable identity/capture/refund triggers; it preserves existing data. Down refuses to discard payment identities.

Simulator terminal: DOTNET_ENVIRONMENT=Development, Simulator__Enabled=true, the three private Simulator secrets above and its database connection. Then:

```bash
dotnet run --project src/OrderFlow.Simulator -c Release --no-build --no-restore --no-launch-profile
```

Worker terminal: merchant database, DOTNET_ENVIRONMENT=Development, PaymentProcessing__Enabled=true, PaymentGateway__BaseUrl=http://127.0.0.1:5090/ and PaymentGateway__ApiKey matching the provider API key. Then:

```bash
dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile
```

Keep payment processing off while arranging a chosen scenario: checkout/login using the existing cookie/CSRF runbook, initiate with an empty body and fresh client key, obtain the provider key from the privately inspected queued record, configure the scenario with the operator credential, then enable/start the Worker. GET the attempt to observe current state. Repeat the same initiation key and compare its original 202 bytes; use a fresh key after Failed only. For timeout, inspect Unknown then the same provider transaction/capture after reconciliation/restart. Browser result fields never set payment/order state.

## Tests, operational limits and interview questions

```bash
# Actual PostgreSQL + a test-owned loopback simulator host; Docker required by default.
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Payment|FullyQualifiedName~SimulatorComposition'
# No Docker/PostgreSQL/simulator network service required:
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

The existing explicit ORDERFLOW_TEST_POSTGRES override may supply actual PostgreSQL instead of Docker; missing prerequisites fail rather than skip or use InMemory/SQLite. The PostgreSQL collection is serial, each case has owned databases, and race participants use independent contexts. Same-order initiation gates are before Order locking SQL. Provider submission gates are before the first advisory lock. Stale-lease tests pause outside merchant transactions; another process can reach that boundary safely. No arbitrary sleep proves contention; one-second HTTP timeouts deliberately test transport uncertainty, while business deadlines/backoff/leases advance logical time.

Coverage: ownership/auth/CSRF/tampering, exact expiry, same/different client-key races, original response replay, failed replacement, real HTTP pending/success/failure/timeouts, amount mismatch, provider restart, stable IDs, concurrent provider idempotency, callback signatures/redelivery/control isolation, full refund replay, uniqueness/immutable DB guards, intent rollback and lost merchant observation/reclaim. An independent connection acquires merchant Order/Attempt NOWAIT locks at gateway invocation, proving the intention is committed and no merchant transaction is held over HTTP.

Limits: no verified capture settlement/consumption/refund intentions, callback authentication endpoint/inbox, callback delivery scheduler, operator replay UI/command, external broker, real payment provider or production deployment claim. Host restart and injected DB-failure tests run in one test process; OS kill/partition/uncertain-commit suites remain future work. Financial uncertainty after retry exhaustion remains visible and blocks replacements until future audited reconciliation, rather than inventing failure.

1. Vì sao lưu attempt trước gọi gateway? Để mất response/restart vẫn tìm được ý định và dùng lại cùng provider key; không giữ transaction qua HTTP.
2. Vì sao timeout không phải Failed? Provider có thể đã capture; tạo attempt mới có thể thu tiền hai lần. Giữ Unknown và query key cũ.
3. Vì sao cần cả lease token và provider idempotency? Token chặn worker cũ ghi đè DB; provider key chống lặp hiệu ứng ngoài DB khi lease hết hạn hoặc response mất.

References: [HttpClient lifetime guidance](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines), [PostgreSQL partial indexes](https://www.postgresql.org/docs/18/indexes-partial.html), [HMACSHA256](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hmacsha256.hashdata?view=net-10.0).
