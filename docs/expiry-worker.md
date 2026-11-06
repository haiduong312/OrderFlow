# Reservation/order expiry worker — milestone 14 expiry slice

The separate Worker host now finalizes overdue PendingPayment orders. Outbox dispatch/inbox consumers, payment settlement/consumption and refunds remain separate work. Expiry records a Pending `ordering.order-expired` event; it does not publish a message or call a network service. See [ADR-017](adr/0017-order-expiry-worker.md), [order management](orders.md) and [test harness](testing.md).

## Code flow and transaction walkthrough

`OrderExpiryWorker` schedules calls to `OrderExpiryIteration.ProcessAsync`. The callable iteration attempts at most BatchSize orders, sequentially. Each attempt creates/disposes an async DI scope and resolves a fresh scoped `OrderExpiryProcessor`/DbContext. No context is shared across attempts, workers or tasks. API never registers the scheduler. Worker additionally offers opt-in dev/test payment processing (see [payments](payments.md)); expiry scheduling remains independent.

For one order:

1. Begin a Read Committed transaction; apply existing local lock_timeout=5s/statement_timeout=20s (provider command timeout 30s).
2. Sample injected normalized UTC and execute exactly this parameterized selection, using the existing partial `(ExpiresAt, Id)` index:

   ```sql
   SELECT * FROM ordering."Orders"
   WHERE "Status" = 'PendingPayment' AND "ExpiresAt" <= @cutoff
   ORDER BY "ExpiresAt", "Id"
   LIMIT 1 FOR UPDATE SKIP LOCKED;
   ```

3. Keep that order lock until commit/rollback. Sample UTC again AFTER locking; `Order.Expire` centrally rechecks PendingPayment/deadline. Equality is expired. Its decision time remains authoritative while waiting for inventory, matching cancellation and future payment settlement. No candidate or an ineligible fresh state ends this iteration without effects.
4. Increment order version, set Expired and create a System history fact. Reuse `OrderReservationRelease` with cancellation: lock balances by canonical product UUID order, then each reservation. Validate quantity/creation/deadline against immutable order items. Release only Active reservations: Reserved decreases, OnHand stays unchanged, balance revision increases and a linked ReleaseStock:v1 movement is appended. An already Released reservation is untouched, including terminal metadata; Consumed on a PendingPayment order, missing reservations or mismatched facts fail visibly and roll back.
5. Flush each release as real SQL inside this transaction. Append OrderStatusHistory and one `ordering.order-expired` OutboxMessage at the new order version. Save and commit. An audit/outbox failure rolls back every earlier SQL write for this order.
6. Dispose the scope before attempting the next order. Earlier committed orders remain committed if a later order fails. There is no transaction covering an unbounded backlog and no batch of order locks held while waiting for inventory.

The order row is the claim; there is no separately committed lease/claimed flag. Another worker skips locked orders and may process others. An empty result can mean remaining due rows are locked; polling discovers them again. Never interpret it as proof the global backlog is empty. A cancellation that already owns the order lock can finish its locked decision; a worker skips it. A cancellation arriving after expiry observes Expired and returns the existing 409 without another release/history/event.

The database is the durable schedule: PendingPayment + ExpiresAt. Process shutdown/connection loss before commit rolls back and makes the order discoverable again. After commit it is no longer eligible, so a restart/uncertain-result retry produces no second release. Reserved bounds, terminal-reservation guards and unique ledger/history/outbox effect identities remain in force.

## Configuration and operation

| Setting | Default | Bounds / meaning |
| --- | --- | --- |
| `OrderExpiry__Enabled` | true | Register scheduler only in Worker; false explicitly disables polling |
| `OrderExpiry__BatchSize` | 25 | 1–100 order attempts per callable iteration |
| `OrderExpiry__PollIntervalSeconds` | 5 | 1–300 seconds between iterations, including full batches |

Invalid bounds fail startup. The first pass runs immediately; subsequent passes use cancellable `Task.Delay` with injected TimeProvider. The timer only wakes the worker; it never records which reservations expire. Cancellation propagates through SQL/lock waits and is checked between orders. Stopping interrupts the polling delay without waiting its full interval.

Known PostgreSQL 55P03/40P01/40001/57014 failures produce structured warning 1401 with SQLSTATE, roll back the current attempt and end that pass with an explicit Contended count. A later scheduled pass rediscovers due work after the poll delay, using a new scope. There is no tight automatic transaction retry. Other errors escape to Generic Host, which logs and stops the worker; fix the data/schema/dependency problem and restart. A problematic oldest order can therefore halt processing instead of being silently discarded. Event 1400 records completed-pass expiry count. No event payload, credential or connection string is logged by this job.

Supply private `ConnectionStrings__OrderFlow`, then from the repository root:

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
dotnet tool restore
dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build
dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile
# In another terminal with the same private DB configuration, run a second worker if desired.
```

Hosts never migrate automatically. Development role/account seed commands explicitly suppress all jobs, including when expiry is enabled. To start an idle Worker without a database for composition checks, use `OrderExpiry__Enabled=false`; that disables processing and is not a readiness/success claim. Normal enabled operation needs the configured migrated PostgreSQL database.

Create an order through the existing authenticated checkout flow, then inspect current owner detail/history and inventory. At/after its deadline, a worker pass persists Expired, System actor with null ActorUserId, release metadata/movements and a Pending event. For deterministic boundary verification use the tests below; no 15-minute real wait, edited deadline, production clock-control endpoint or unauthenticated test route is needed.

Migration **20261106081044_AddSystemOrderExpiry** changes history ActorUserId to nullable, permits only PendingPayment -> Expired System history with null user, and permits System expiry Release movements. Existing human facts/FKs/append-only triggers remain intact. No fake Identity service account is created. Current history JSON may contain `actorKind: "System", actorUserId: null`; coordinate clients and API/Worker versions before starting the job. Old binaries with nonnullable history actors must not read newly inserted System facts. Down refuses to fabricate human UUIDs or discard audit facts; use a reviewed forward migration. Implementation verification migrated only owned disposable test databases.

## Automated verification and limits

```bash
# Docker required unless ORDERFLOW_TEST_POSTGRES privately targets actual PostgreSQL 18.
dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrderExpiryTests'
# No Docker/external services required:
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
```

Tests cover deadline -1/0/+1 microsecond, not-yet-due/other-status/unrelated rows, bounded backlog/resume, repeat processing, two independent Worker hosts sharing products, a separately held due order, cancellation overlap including a pre-deadline locked decision, already Released facts, outbox failure after real release SQL, classified contention/fresh-scope recovery, scheduled shutdown mid-release/restart, and interruption of a 300-second poll delay. Fresh reads reconcile Active quantities and ledger sums against every balance, release uniqueness, versions/history and outbox counts.

Two-worker tests rendezvous after two DIFFERENT orders are claimed via SKIP LOCKED, before either holds inventory. Same-order cancellation/expiry rendezvous BEFORE their order statements. The pre-deadline cancellation pause is one-way: test observes it, runs the worker (which skips), then releases cancellation; it never demands a blocked contender reach an inventory barrier. All hooks live in test interceptors; waits are bounded and no sleeps claim race correctness.

The PostgreSQL collection remains serial with a migrated database per case; races explicitly create independent contexts/hosts. Tests require Docker by default, fail with the actual prerequisite diagnostic if unavailable, and never substitute EF InMemory/SQLite. Host restart tests prove committed-backlog and cancelled-transaction recovery in one test process, not OS-kill/network-partition recovery. Real elapsed timeout/deadlock injection, verified payment races, external delivery, worker readiness/lag monitoring and runtime least-privilege deployment remain later milestones. Host clocks must remain synchronized; processing lag conservatively retains stock until a safe transition commits.

## Giải thích và câu hỏi phỏng vấn

Worker chỉ đánh thức việc kiểm tra; PostgreSQL giữ lịch hết hạn qua trạng thái và ExpiresAt. Mỗi order có một transaction và khóa riêng. Đến đúng deadline, worker đổi trạng thái, giải phóng phần giữ hàng còn Active, ghi audit/event rồi commit cùng nhau. Dừng trước commit thì rollback; khởi động lại sẽ tìm lại order còn PendingPayment.

1. Vì sao dùng SKIP LOCKED? Để hai worker không cùng sở hữu một order và không phải chờ order đang do cancellation/worker khác xử lý; lần sau vẫn kiểm tra lại hàng bị bỏ qua.
2. Vì sao mỗi order có một transaction và khóa order trước inventory? Để giới hạn rollback/khóa và giữ cùng thứ tự phối hợp với cancellation/payment, tránh vòng chờ do lấy khóa ngược.
3. Worker chết giữa release và outbox thì sao? Cả transaction chưa commit rollback; lần chạy sau tìm lại PendingPayment. Nếu đã commit, trạng thái terminal và các định danh effect ngăn release lần nữa.

References: [PostgreSQL SELECT / SKIP LOCKED](https://www.postgresql.org/docs/18/sql-select.html), [scoped BackgroundService dependencies](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service), [TimeProvider-aware Task.Delay](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay?view=net-10.0).
