# ADR-017: One-order SKIP LOCKED expiry transactions in the separate Worker host

Date: 2026-11-06. Status: Accepted. Extends ADR-003/013/014/016. Delivers only the expiry-worker slice of roadmap 14; dispatch/inbox processing remains pending.

## Decision

Use existing PendingPayment/ExpiresAt data and the partial due-order index as the durable schedule. OrderExpiryWorker schedules a callable bounded OrderExpiryIteration, creating a fresh async scope/DbContext for each OrderExpiryProcessor attempt. Each transaction selects at most one due order with ORDER BY ExpiresAt, Id LIMIT 1 FOR UPDATE SKIP LOCKED. Recheck state/deadline under that order lock using injected normalized UTC; equality expires. Retain the decision time while awaiting inventory, as cancellation and future settlement require.

Reuse OrderReservationRelease across customer cancellation and system expiry. Acquire Order before all balances in canonical UUID order, then corresponding reservations. Never claim a multi-order batch and hold its locks while waiting for inventory. Release only Active reservations, preserving already Released metadata; reject missing/mismatched/Consumed facts on a pending order rather than silently manufacturing a valid payment state. All order/version/reservation/counter/ledger/history/outbox effects commit or roll back together. No network call occurs inside this transaction.

Add System actor with null human ID to status history and expiry release movements through an additive migration. Human history actors remain required; System history is restricted to PendingPayment -> Expired. Preserve previous rows, restrictive FKs and append-only/terminal guards. History DTO ActorUserId becomes nullable; coordinate old API/Worker/client versions before enabling System writes. No fabricated Identity account. Refuse destructive downgrade.

The order lock itself is the claim. Rollback/disconnect releases it and leaves durable work due; successful commit makes the order ineligible. Polling may temporarily see no unlocked candidate while other work is held; a later pass revisits it. Default 25 attempts/pass, allowed 1–100; 5-second delay between passes, allowed 1–300. SQL/lock budgets and cancellation bound waits. Known contention is reported/logged and ends the pass; unexpected failures stop the worker visibly. Seed commands suppress jobs; API has no scheduler; tests explicitly drive iterations or start/stop the real scheduler.

## Alternatives and verification

A database lease would require owner/token/expiry state and recovery without benefit for this entirely database-local transition. A local timer per reservation would lose durable schedule across restarts. An unbounded batch transaction extends lock/rollback scope. In-process locks do not coordinate API and independent workers.

Actual PostgreSQL tests cover exact logical-time boundaries, independent hosts, cancellation overlaps, active-only release, bounded passes/restarts, rollback after executed release SQL and graceful shutdown mid-transaction/delay. Safe test-only synchronization never waits for a contender behind a held lock it needs. Ledger/Active reservation sums and unique releases/history/events are asserted. OS-kill/connection-loss fault suites, payment settlement and outbox publishing remain future scope; no exactly-once external delivery claim is made.

See [expiry worker walkthrough/runbook](../expiry-worker.md) and [implementation status](../implementation-status.md). References: [PostgreSQL SKIP LOCKED](https://www.postgresql.org/docs/18/sql-select.html), [scoped hosted work](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service), [cancellable TimeProvider delay](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay?view=net-10.0).
