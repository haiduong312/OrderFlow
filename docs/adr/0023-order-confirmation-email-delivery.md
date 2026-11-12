# ADR-023: Durable order-confirmation email intentions and separate sending

Date: 2026-11-12. Status: Accepted. Extends ADR-022's local-notification-only scope with the explicitly requested optional development email workflow. Payment/order eligibility and broker delivery semantics remain unchanged.

## Context

Order confirmation must not depend on SMTP. A consumer can receive the same confirmation concurrently; a sender can lose its result after the provider accepted a message. PostgreSQL cannot atomically commit an external email send. SMTP Message-ID is not an idempotency contract.

## Decision

Extend the existing order-event consumer: for supported `ordering.order-confirmed` only, create EmailDeliveries in the same PostgreSQL transaction as the notification, consumer-specific ProcessedMessages and NotificationCreated outbox. The existing commit-before-RabbitMQ-ACK boundary remains. An INSERT ON CONFLICT on `(OrderId,Template)` plus unique `(SourceEventId,Template,RecipientUserId)` enforces one confirmation intention, including different source identities for the same historical order confirmation. No sending occurs in the consumer or checkout/settlement transaction.

Snapshot recipient from the authoritative owner's Identity email at intention creation, not broker/request input. Snapshot from/template/subject/plain-text body (server-recorded USD total), stable delivery ID, provider name/key, MIME Message-ID and request hash. Later account/template changes cannot change a queued request. Invalid/missing recipient creates NeedsReview without failing notification processing. SQL freezes identities/snapshots and Sent evidence; restrictive FKs/confirmation-history guard protect association. Existing already-Processed confirmation events are not automatically backfilled; no deletion/reset of old markers.

IEmailGateway declares SupportsIdempotentSend/SupportsLookup. Sending claims one due Pending/expired InFlight row with SKIP LOCKED in a short transaction, persists unique owner/token/60-second expiry, counters and conservative OutcomeUncertain=true before any possible network effect, then commits/disposes scope. Call gateway outside transactions under a 20-second combined budget. Fresh result scope uses one conditional UPDATE requiring same owner/token/unexpired lease. Lost/stale owners cannot acknowledge newer work. Network/shutdown/unknown errors cannot become fake Sent.

Eight attempts per automatic cycle maximum, 5/15/30/60/120/300/600/600-second scheduling, bounded exhausted-lease cleanup and 20 items/pass/five-second cancellable polling. Permanent errors, invalid response correlation, provider rejection and exhaustion become NeedsReview; unknown exceptions stop visibly with a recoverable lease. Total AttemptCount never decreases; per-cycle AttemptsInCycle resets only through an audited Admin retry. Status is Pending/InFlight/Sent/NeedsReview; Sent means gateway/server accepted the handoff, not recipient delivery/read.

Lookup first when supported, using original provider key and immutable request digest. Correlate result key/hash/status/provider message ID. Sent is final; Pending is queried later without blindly sending. Idempotent send retries the same key/body. Lookup Absent after a previously uncertain send without idempotent send goes to review: absence alone cannot rule out a stale in-flight operation committing later. Provider-dependent safety requires durable key/request binding, retention throughout automatic/manual recovery, monotonic results and authoritative lookup; no universal external exactly-once claim.

Default adapter is **DevelopmentSmtp**, restricted to Development/Testing, numeric loopback address, `.invalid` recipients, no authentication/relay and bounded timeout. Use BCL .NET 10 SmtpClient.SendMailAsync(MailMessage,CancellationToken) only for this local sink adapter; no new NuGet package. Production/provider integration is not added. SMTP has neither idempotent send nor authoritative status lookup. Reusing key/Message-ID preserves identity but does not prevent duplicates. A crash after SMTP acceptance before local Sent can send a duplicate on lease recovery; an actual Mailpit test demonstrates two accepted messages. Uncertainty is retained when unresolved and exposed to operators.

Development Compose adds Mailpit **v1.33.1**, pinned by pulled digest, loopback SMTP/UI, persistent named volume, no relay, quiet logging, version-check disabled and duplicate-ID suppression explicitly disabled. Only synthetic `.invalid` test/development data is sent. SMTP exceptions are translated into controlled codes; recipients/subjects/bodies/credentials are excluded from application logs and failed-delivery API DTOs.

Admin-only GET /api/admin/email-deliveries lists bounded failed/review metadata. CSRF-protected POST /{id}/retry accepts operationId/reason/acknowledgeDuplicateRisk, locks delivery first, persists an immutable operation audit and requeues the same ID/key/snapshots with a new bounded cycle. Replay of the same operation returns current metadata without rescheduling; changed facts/actor conflict. Sent/Pending/InFlight reject new retry operations. Uncertain work requires explicit duplicate-risk acknowledgement; blank recipient requires forward repair, not a blind retry. SQL requires matching durable cycle audit for a cycle reset. Role checks remain server-side; no snapshot-edit or mark-Sent endpoint exists.

## Consequences and scope

Notifications:Enabled runs the shared order consumer/intention writer; EmailSending:Enabled independently runs SMTP delivery. Disabling/stopping mail delays delivery while order/payment/notification transactions continue. Admin APIs do not perform network delivery. There is no email for late unallocated captures, cancellation/fulfillment events, welcome campaigns or general customer refunds. No realtime/UI, production mail provider, historical backfill, snapshot-editing or general outbox replay is added.

Delivery is at least once for the SMTP sink. Sent evidence/attempts/audits are retained; destructive downgrade/pruning is refused. One-node local sink persistence and controllable in-memory test provider are not production durability/HA claims. See [runbook and outage demo](../email-delivery.md), [verification status](../implementation-status.md).

## Version-sensitive sources

- [Microsoft .NET 10 SendMailAsync overloads](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.sendmailasync?view=net-10.0): async/cancellable local SMTP transport.
- [Mailpit v1.33.1 release](https://github.com/axllent/mailpit/releases/tag/v1.33.1), [official Docker](https://mailpit.axllent.org/docs/install/docker/) and [runtime options](https://mailpit.axllent.org/docs/configuration/runtime-options/): image, persistent store, duplicate-ID flag and quiet mode.
- [Pinned release Dockerfile](https://github.com/axllent/mailpit/blob/v1.33.1/Dockerfile): `/mailpit readyz` health check.
