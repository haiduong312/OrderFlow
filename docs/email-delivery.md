# Order-confirmation email delivery and outage demo

Order confirmation commits independently of mail. The order-event consumer saves a durable email intention; a separate sender calls the gateway outside transactions. Local SMTP acceptance is recorded as Sent, not as proof of recipient delivery/read. SMTP retries can duplicate mail after an uncertain send; no exactly-once claim.

The optional local [Docker deployment overlay](deployment.md) additionally permits exactly the mailpit container hostname through an explicit flag. Development/Testing restrictions and .invalid-only recipients remain. This narrowly extends the host-only numeric-loopback examples below; no production email adapter is added.

## Setup

Use existing [PostgreSQL](persistence.md), [RabbitMQ](messaging.md), [notification consumer](notifications.md) and [payment simulator](payment-recovery.md) private configuration. No package/SDK changes; .NET 10 async/cancellable local SMTP adapter. Explicitly migrate **20261112082134_AddEmailDeliveries** before enabling new code:

```bash
dotnet tool restore
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
docker compose --profile email up -d --wait mailpit
export DOTNET_ENVIRONMENT=Development
export Notifications__Enabled=true
export OutboxPublishing__Enabled=true
export EmailSending__Enabled=true
export EmailSending__Host=127.0.0.1
export EmailSending__Port="${ORDERFLOW_SMTP_PORT:-1025}"
export EmailSending__TimeoutSeconds=10
dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile
```

Privately supply ConnectionStrings__OrderFlow/RabbitMq__Password as in linked guides. Compose configuration still requires ORDERFLOW_DB_PASSWORD_FILE during interpolation, even for mailpit alone; its secret is not mounted into Mailpit. SMTP/UI ports bind loopback only (1025/8025; ORDERFLOW_SMTP_PORT / ORDERFLOW_MAIL_UI_PORT overrides). Mailpit v1.33.1 is digest-pinned, stores data in mailpit_data, has no relay configured, disables external version check and explicitly allows duplicate Message-IDs. `down` without --volumes keeps its store. Do not delete messages/volumes to repair a sender issue.

Only Development/Testing hosts may enable the provided adapter; only numeric loopback SMTP and `.invalid` recipient domains are allowed. Use existing generated `browser-...@example.invalid` identities or synthetic development accounts. This adapter intentionally has no SMTP credentials/TLS/production provider support. Production delivery requires a separately implemented/reviewed gateway and provider contract. Mailpit UI is a local development inspection tool, not the Admin application.

Consumer and sending flags are independent. Notifications:Enabled writes the intention from new confirmed events even while EmailSending is disabled. API/checkout/payment settlement do not resolve or call IEmailGateway. Seed commands suppress sender loops. Account email changes after intention creation do not alter its immutable recipient snapshot.

## State/identity and network boundary

EmailDeliveries retains:

- ID, source EventId, order/owner IDs; unique order/template and source/template/recipient.
- Recipient/from/template/subject/plain-text body snapshots, provider name/key, MIME Message-ID, request hash.
- Pending/InFlight/Sent/NeedsReview, created/updated/due/sent/failure times, safe error, provider message ID and uncertainty.
- Monotonic lifetime attempts, attempts in current automatic cycle, manual retry count, owner/token/lease.

Only ordering.order-confirmed v1 creates a confirmation intention. Sender uses server-owned order total and stored owner email, never a frontend total/paid flag or broker-supplied email. Consumer keeps its existing notifications.orders.v1 inbox identity; email, notification, handled inbox and NotificationCreated outbox commit together before broker ACK. A different source ID for the same confirmation cannot create a second email. Invalid/missing address creates review metadata without failing the order-event consumer. Historical already-Processed events are not retroactively rerun; reviewed backfill remains future work.

Claim: one row with PostgreSQL FOR UPDATE SKIP LOCKED; increment both attempt counters and persist owner/token/60-second lease plus conservative OutcomeUncertain=true, then commit/dispose. Network: lookup if supported, otherwise/send when safe, outside SQL under a combined 20-second budget (local SMTP timeout configurable 1–20 seconds). Complete: fresh context, conditional UPDATE requiring matching live owner/token. An old owner cannot set Sent after another claim or expiry. Shutdown/unknown failures leave the claim and uncertainty for recovery. No DbContext is shared between parallel workers.

Eight attempts per cycle maximum, with 5/15/30/60/120/300/600/600-second backoff. A crash on the eighth attempt becomes NeedsReview after lease expiry through separately bounded cleanup (20 rows). Each worker pass processes up to 20 sequential deliveries, then cancellable five-second polling. Only classified transport/4xx failures retry; 5xx rejection/protocol/recipient/mismatch/exhaustion require review. Unknown exceptions stop visibly. SMTP connection/timeout failures conservatively retain uncertainty; 4xx/5xx rejection alone is classified without claiming acceptance.

Gateway capabilities are explicit. Lookup results must match original provider key/request hash; Pending schedules lookup, Rejected reviews, Sent becomes terminal. Idempotent providers must durably retain key/body/result and return the same effect on retry throughout the recovery window. Lookup-only Absent after prior uncertainty requires review because a stale in-flight operation may later commit. SMTP has no lookup/idempotency: it can resend the same immutable request/key/Message-ID after uncertainty and produce duplicates. The stable key is not an SMTP server guarantee. A future gateway must not map provisional failure to final rejection or change identity to repair uncertainty.

## Admin failed metadata and audited retry

Admin-only cookie API, no-store responses:

| Route | Behavior |
| --- | --- |
| GET /api/admin/email-deliveries?page=1&pageSize=20 | Failed/review metadata, page size <=100; excludes address, subject, body and request hash |
| POST /api/admin/email-deliveries/{id}/retry | CSRF + `{operationId,reason,acknowledgeDuplicateRisk}`; requeue same delivery/key/snapshots |

Retry requires NeedsReview. Sent/Pending/InFlight reject a new operation with 409. Missing delivery is 404; missing Admin role 403 (anonymous 401), missing CSRF 400. Blank recipient cannot be repaired by blind retry; immutable snapshot repair/backfill needs a reviewed future operation. Known permanent environment/provider rejection can be retried after fixing the cause, without editing snapshots.

Use a new operationId for each deliberate operator action. Same operation/actor/delivery/trimmed reason/risk flag replays current metadata without another retry cycle; changed facts under that ID return 409. Uncertain work requires acknowledgeDuplicateRisk=true. The audit stores actor, time, reason, cycle and risk acknowledgement; it does not create a replacement email. It persists in the same transaction as resetting AttemptsInCycle to zero/incrementing ReviewRetryCount; lifetime AttemptCount remains monotonic. SQL requires the matching cycle audit and freezes successful/identity/snapshot/audit facts. Replay after Sent returns current Sent without sending again; a new retry after Sent is forbidden.

Application log events: 2001 controlled gateway code/attempt/uncertainty and delivery ID; 2002 lost-lease result ignored; 2003 database contention. No SMTP exception text, address, subject, body or credentials are logged. Admin metadata exposes stable IDs/scheduling/error, not message contents. Do not paste private snapshot fields into logs/issues/operator reasons.

## Demo: mail off while orders/payments work

Deterministic executable demo with actual PostgreSQL, independent simulator HTTP and real Mailpit:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~EmailServiceOutageTests'
```

It stops the actual mail container **before checkout**, asserts checkout 201 and payment initiation 202, submits/looks up a real simulator capture through shared settlement, checks Confirmed/consumed stock/no refund, processes the confirmed event, and observes Pending email while SMTP is down. After restarting sink and advancing injected due time, the same intention becomes Sent and exactly one message is found for that synthetic recipient. The fixture refreshes Docker's randomly assigned port after restart; Compose uses a fixed host port and needs no such test override. No real payment provider/customer email is used.

Interactive Compose demo:

1. Start API, restricted simulator, broker and Worker using existing private guides. Enable PaymentProcessing, PaymentReconciliation (PaymentSettlement for callbacks), Notifications, OutboxPublishing and EmailSending. Use a `.invalid` owner email.
2. `docker compose --profile email stop mailpit`.
3. Through existing authenticated/CSRF checkout/payment APIs, create an order and initiate a simulator Succeeded payment (or deliver its signed callback). GET order eventually shows Confirmed and stock consumed. Email sending must not affect the original checkout/accepted payment responses.
4. Inspect metadata below: one Pending email, failure/backoff/uncertainty, original key and no SentAt. Repeated confirmed delivery must not add another intention.
5. `docker compose --profile email start --wait mailpit`. Await NextAttemptAt and observe the same ID/key become Sent. View the synthetic message at `http://127.0.0.1:8025` (or configured UI port).
6. For permanent/exhausted work, sign in as Admin, obtain fresh CSRF token, GET failures and POST a reviewed retry using the same delivery ID, a stable operationId and risk acknowledgement when needed. Do not manually mark Sent or reset counters.

Read-only metadata queries:

```sql
SELECT "Id","OrderId","Template","ProviderKey","Status","AttemptCount","AttemptsInCycle",
       "NextAttemptAt","LeaseExpiresAt","OutcomeUncertain","LastErrorCode","SentAt"
FROM notifications."EmailDeliveries" ORDER BY "CreatedAt" DESC LIMIT 100;
SELECT "OperationId","DeliveryId","ActorUserId","CycleNumber","RequestedAt","AcknowledgeDuplicateRisk"
FROM notifications."EmailDeliveryRetries" ORDER BY "RequestedAt" DESC LIMIT 100;
```

If the outage exceeds eight automatic attempts, restore SMTP then use reviewed Admin retry. A gateway timeout or crash after send can already have created a message: **SMTP recovery may create another email**, even with the same Message-ID. The test `Actual_SMTP_sink_can_receive_duplicate_after_local_result_is_lost` demonstrates two accepted messages, while idempotent/lookup fake-provider tests demonstrate one effective send under that provider contract. Do not describe either a SMTP reply or Sent as end-user read/delivery proof.

## Verification and limits

Focused command:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Email'
```

Tests cover atomic duplicate intention, same-order/different-event identity, rollback, snapshot stability, transient/permanent failure, request correlation, concurrent claims, expired owner, lost local acknowledgement, shutdown after acceptance, bounded exhaustion, pending/rejected lookup, actual SMTP duplicates, Admin roles/CSRF/audit replay and outage/payment independence. Independent NOWAIT probes at lookup/send prove no merchant row transaction is held. See [status](implementation-status.md) for exact executed results.

No general customer email, production provider, delivery/read tracking, general notifications/realtime UI, snapshot edit, historical backfill, provider key-expiry handling or independent OS-kill/cluster/partition guarantees. The controllable provider store outlives merchant service scopes within the test process; production guarantees require a durable provider store/retention contract. Data/evidence are retained and destructive downgrade is refused; future archival needs review.
