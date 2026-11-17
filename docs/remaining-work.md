# Remaining work and deliberate v2 backlog

This list separates unfulfilled parts of the existing contract from optional future features. [Audit](acceptance-audit.md) is the completion authority; old milestone prose records history, not blanket production readiness.

## Still incomplete in the existing contract / operating scope

| Item | Current behavior and next bounded step |
| --- | --- |
| Standalone stock integration events | `InventoryService` commits balances+immutable ledger for receipts/adjustments, but emits no inventory outbox event. Add a versioned event in that transaction with replay/rollback tests before declaring full stock-event A16 complete. The originally listed refund-failed integration event is also absent; NeedsReview is visible through persisted refund state/audit, not a failure event. Order reserve/release/consume already share their order/payment outbox transaction. |
| Enriched order envelope / refund failure events | `OrderEvents.Create` includes identity/owner/state/version/total/created/expiry, not immutable confirmedAt/winning capture reference required by original section7. Direct reports use authoritative captures/items under ADR-026; no broken reporting projection depends on the missing fields. Specify compatibility/upcast/migration only if adding required facts; do not invent them from current fulfillment time. |
| General operational failures / safe replay | `/api/admin/operations/failures` is not mapped; no generic failed-job/outbox/refund paginated screen or retry API. Email metadata/audited same-ID retry and per-order read-only payment/refund audit exist. Add narrow typed policies/CSRF/audit/state guards/original identities when requested, not unrestricted execution or counter resets. |
| Remote CI acceptance | Workflow/scripts/action pins/local checks exist. First actual GitHub run, branch protection and clean hosted browser provisioning remain unverified; no push is performed here. |
| Production lifecycle | No public deployment, certified provider/email, multi-host shared-key storage, certificate rotation/old-key decryption set, secret-manager encryption, tested backups/restores, rolling-schema compatibility, HA or incident operation is delivered. Current Compose is loopback-only, single dependencies and local volumes. |
| Failure/load coverage limits | Deterministic post-commit ACK/channel/SQL failpoints are tested; exact OS kill at that instruction, multi-host partitions, prolonged saturation and real-provider behavior are unmeasured. Local bounded RPS is not capacity. |

The new explicit `initialize-keys` one-off addresses the documented **empty-ring local bootstrap prerequisite**, before either API serves sessions. It does not prove arbitrary live key revocation/rotation/rolling restarts safe. API rate limiting is per-process, not a cluster-wide quota. RabbitMQ quorum queues on one broker are not HA; the foundation catch-all queue is not a general business consumer and needs an explicit retention/drain/operations decision before sustained usage.

## Deliberate v2 candidates, ordered by demonstrated need

1. **Operations first:** reviewed typed recovery for NeedsReview payments/refunds and failed outbox/inbox, original work identities and audits; distinguish retry-safe lookup from an irreversible new external effect. Add backlog/consumer progress alerts and a backup/restore/secret-rotation drill.
2. **Real provider adapter only with a chosen provider:** durable webhook/reconciliation semantics, capability contract, timeout reconciliation, signed key rotation and duplicate capture/refund tests in a sandbox. Real email adapter must document idempotency/lookup limitations. No card handling in this app.
3. **Measured scale:** representative larger data/workload, repeat distributions, multi-host network/failure tests and generator headroom. Inspect query plans before indexes, connection/poll budgets and queue retention. Add projections only if direct reports demonstrably need them; retain replay/dedup/as-of contracts if introduced.
4. **Product additions only after correctness/operations:** shipping addresses/carrier integration, returns/partial refunds, discount/tax/multicurrency, richer catalog media or guest checkout each require new money/security/state-machine rules. They are not disguised as existing v1 features.

Not selected merely for a portfolio: microservice rewrite, event sourcing, Kafka, generic CQRS framework or a homemade JWT/refresh-token system. Keep the current understandable boundaries until an actual constraint justifies changing them.
