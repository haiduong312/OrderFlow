# OrderFlow implementation status

Updated: 2026-10-30 (Asia/Ho_Chi_Minh). Current milestone: **01 — implementation contract**. Only documentation is complete. There are no application projects, frontend manifests, database migrations, or executable business features yet.

## Repository inspection

- Workspace: `/home/DUONGNH1/Desktop/Order Processing System`.
- No AGENTS.md was found in the workspace or inspected ancestor locations. `.agents`, `.codex`, `.aws`, and `.git` were empty directories; no repository instructions or existing work were available to adapt.
- `rg --files --hidden --no-ignore .` found no files before this milestone. No source, tests, package versions/lockfiles, migrations, specifications, architecture, ADRs, or pre-existing 01–29 roadmap existed.
- `git status --short --branch` exited 128: `fatal: not a git repository (or any of the parent directories): .git`. Git diff/status validation is unavailable. Do not infer a clean working tree. No Git initialization, commit, push, history rewrite, or deployment was performed.
- Available: .NET SDK 10.0.114, ASP.NET Core/.NET runtimes 10.0.14, Node v24.19.0, npm 11.17.0, Python 3.12.3. `docker` is on PATH; daemon/container/PostgreSQL execution was not tested. `psql` is not on PATH. No packages were installed and no version lock was invented.

## Milestone 01 acceptance

| Criterion | Result | Evidence |
| --- | --- | --- |
| D01: Inspect repository/instructions/code/packages/migrations/Git | Complete | Inspection above, including explicit Git limitation |
| D02: Practical behavior/scope contract | Complete | [project-spec.md](project-spec.md): roles, endpoints, normalization/replay, money/time, explicit transitions, edge cases and deferred scope |
| D03: Architecture and durable boundaries | Complete | [architecture.md](architecture.md): feature folders, schemas, shared scoped context, SQL guards, locks, retries, event/lease boundaries |
| D04: Dependency map 01–29 and invariants/tests | Complete | Roadmap below and specification acceptance matrix A01–A24; runtime criteria still pending |
| D05: Initial ADRs and document consistency | Complete | [ADR index](adr/README.md), six accepted decisions; document checks recorded below |
| D06: Documentation-only change | Complete | Ten Markdown files, no source/package/migration/business implementation |

No contradiction with an existing contract was found because none existed. Newly fixed edge cases include deadline equality and locked decision time, no deadline extension, overdue cancellation becoming Expired, replay of original bytes after later status changes, one unresolved attempt/order, monotonic capture evidence, compensation for surplus/mismatched captures, conservative reads during expiry lag, immutable catalog snapshots, no shipping address/fee, and report revenue from the winning confirmation only.

## Milestones 01–29 and dependencies

This is the initial roadmap established here, not an externally supplied milestone sequence. Dependencies are prerequisite milestone IDs, not instructions to implement every later feature now. Each milestone must update this table with checks, changed files/migrations, remaining criteria, Vietnamese explanation, manual steps, trade-offs, three interview questions, and its suggested commit title. Numbers are stable; later scope changes require updating the dependency map explicitly.

| ID | Deliverable / completion gate | Depends on | Criteria | Status |
| --- | --- | --- | --- | --- |
| 01 | Inspect workspace; establish spec, architecture, status and initial ADRs | — | D01–D06 | Complete: documentation only |
| 02 | Scaffold API/Application/Worker and test projects; pin supported SDK/NuGet/tools; build | 01 | A01 | Pending |
| 03 | PostgreSQL local setup, shared context, owner schemas, initial migration/test database harness | 02 | A01, A16 | Pending |
| 04 | React/TypeScript skeleton; pinned client tooling, TanStack Query/Zustand, dev same-origin proxy | 02 | A01 | Pending |
| 05 | Identity schema, Customer registration/login/logout/session, safe Admin bootstrap and cookie settings | 03 | A02 | Pending |
| 06 | Antiforgery, API auth errors, roles/ownership conventions and HTTP test helpers | 05 | A02 | Pending |
| 07 | Catalog domain/admin CRUD/deactivation; exact money JSON, versions, product snapshots | 03, 06 | A03 | Pending |
| 08 | Inventory balances/history schema, guarded adjustments and ledger constraints; reservation domain rules | 07 | A04, A07, A16 | Pending |
| 09 | Admin stock HTTP endpoints, operation replay, stock races and constraint tests | 08 | A04 | Pending |
| 10 | Database command-idempotency primitives, canonical bodies and exact response replay storage support | 03, 06 | A06 | Pending |
| 11 | Order/line/reservation schema and domain, deadline rules, minimal transactional outbox storage and state tests | 07, 08 | A03, A07, A08 | Pending |
| 12 | Atomic CreateOrder/reserve + original response; stock/idempotency concurrency and rollback | 09, 10, 11 | A05–A08, A16, A23 | Pending |
| 13 | Own order reads/list, pending cancellation/release and authorization regressions | 12 | A02, A07–A09 | Pending |
| 14 | Outbox dispatch/inbox and worker lease/retry foundation; expiry worker + deterministic races | 13 | A07–A09, A16–A19 | Pending |
| 15 | Payment attempt/intention schema, request idempotency, active-attempt constraint, customer start/read | 10, 14 | A12, A13, A23 | Pending |
| 16 | Durable gateway simulator, submission/query worker, independent transaction boundary and uncertainty | 15 | A12, A13, A19, A23 | Pending |
| 17 | Signed callback receipt/inbox, verification, duplicate/quarantine handling and replay tests | 16 | A13, A14 | Pending |
| 18 | Atomic capture confirmation/stock consumption; late capture compensation/refund reconciliation and races | 17 | A07–A13, A16, A17, A19, A23 | Pending |
| 19 | Admin fulfillment state machine, expected versions, order inspection and authorization | 13, 18 | A02, A15 | Pending |
| 20 | Browser session/CSRF flow, active catalog, cart and exact cents state | 04, 06, 07 | A03, A20 | Pending |
| 21 | Checkout UI, pending key/body recovery, authoritative order/deadline/payment/cancel views | 18, 20 | A03, A20 | Pending |
| 22 | Admin catalog/stock/order/fulfillment UI with state/version conflict handling | 09, 19, 20 | A20 | Pending |
| 23 | Deduplicated local notifications consumer, durable deliveries and failure visibility | 14, 18 | A17, A18, A21 | Pending |
| 24 | Versioned reporting projection, daily winning sales, range/auth tests and report UI | 19, 20, 23 | A17, A18, A21 | Pending |
| 25 | Reproducible API/client/worker/proxy local composition, migration command, runtime DB roles and readiness | 22, 24 | A01, A16, A22 | Pending |
| 26 | Multi-worker/crash/uncertain commit/stale lease fault suite; retry and no-network-in-transaction evidence | 18, 23, 24, 25 | A12, A17–A19, A23 | Pending |
| 27 | Browser acceptance flows; cross-user/CSRF/auth regressions; structured redaction/config checks | 21, 22, 25, 26 | A02, A20, A22 | Pending |
| 28 | Operational backlog/failure view, health/lag metrics, audited same-identity operator replay and recovery runbook | 25, 26, 27 | A19, A22 | Pending |
| 29 | Full acceptance walkthrough; portfolio architecture/demo/interview guide and verified limitations | 28 | A01–A24 | Pending |

Feature order is intentional: HTTP idempotency/stock correctness precede payment; outbox/worker durability precede external intentions; auth/CSRF precede protected client flows; fault tests and operability precede the final demo. Reservation/order foreign keys are introduced together in 11; command-to-order links are completed in 12 after both schemas exist. Minimal outbox persistence in 11 makes checkout atomic in 12, before dispatch is implemented in 14. Each feature milestone writes its relevant unit/SQL/HTTP tests then; milestone 26 is cross-feature fault coverage, not permission to delay all concurrency testing.

## Checks and limitations for milestone 01

Executed inspection: `pwd`, `ls -la`, `rg --files` (including hidden/no-ignore inventory), `find . -maxdepth 3 -type f -print`, ancestor instruction-file checks, `git status --short --branch`, executable discovery, `dotnet --list-sdks`, `dotnet --list-runtimes`, `node --version`, `npm --version`, and `python3 --version`. Empty `rg` inventory exited 1 because no files matched; this is not a source/build failure. Git failed as recorded above.

Documentation verification used `python3 /tmp/orderflow_doccheck.py` (temporary checker outside the deliverables), exit 0, **PASS**: 10 expected nonempty Markdown-only files, 25 valid relative file/anchor links, 10 tables with consistent column counts, balanced fenced blocks, six indexed accepted ADRs, all 30 criteria D01–D06/A01–A24, contiguous roadmap IDs 01–29 with valid earlier dependencies/no cycles and complete criterion coverage, and no trailing whitespace/conflict markers/unresolved implementation placeholders. This checks document structure and coverage, not business correctness or runtime behavior. Manual content review checked scope, lifecycle/inventory arithmetic, deduplication identities, time boundaries, and roadmap prerequisites against the supplied milestone.

No `dotnet build`, `dotnet test`, frontend build/test, SQL migration, PostgreSQL integration test, browser test, or container smoke test ran: the repository has no application/solution/package manifests or migrations. Their behavior is unverified, not passing. Network access was used only to consult official .NET, ASP.NET Core, EF Core and PostgreSQL documentation; no application API signatures were implemented. Patch/package/container pins remain required before their first use in milestones 02–04/25.

## Manual review and next work

From the workspace:

```bash
find docs -type f -name '*.md' -print
cat docs/project-spec.md
cat docs/architecture.md
cat docs/implementation-status.md
cat docs/adr/README.md
rg -n '^\| (0[1-9]|1[0-9]|2[0-9]) \|' docs/implementation-status.md
rg -n '^\| (D0[1-6]|A[0-9]{2}) ' docs/project-spec.md
```

Review these concrete scenarios against the tables: two buyers contend for one available unit; same checkout key replays after cancellation; callback is processed exactly at ExpiresAt; cancellation wins before a delayed capture; callback delivers the same capture twice under different event IDs; two workers resume an expired refund lease; an old reporting event follows a new one. Expected outcomes must preserve stock, capture evidence, and one durable effect per identity.

Remaining work: all milestones 02–29 and runtime criteria A01–A24. Next is scaffold/version pinning only (02), followed by real PostgreSQL setup/harness (03). Git repository setup is an explicit user/operator task; it was not silently performed here. No migration was created in milestone 01.

Trade-offs: shared context and database simplify atomic correctness but couple releases; explicit locks constrain throughput; polling creates lag/duplicates; simulator does not validate a real provider; original responses/history require storage; host time needs synchronization. These are accepted v1 choices, not hidden guarantees.

## Vietnamese explanation and interview preparation

Milestone 01 thiết lập hợp đồng triển khai, chưa có chức năng chạy được. API và worker là hai tiến trình dùng chung một application project và một PostgreSQL. Mỗi module sở hữu schema riêng; một unit of work có thể gọi các module để cập nhật đơn, tồn kho, lịch sử và outbox trong cùng transaction.

Luồng chính dự kiến: tạo đơn kiểm tra người dùng và idempotency key, lấy giá từ server, giữ tồn kho đúng 15 phút rồi lưu phản hồi 201 gốc. Thanh toán đi qua ý định đã lưu, worker gọi gateway ngoài transaction. Khi xử lý capture hợp lệ trước hạn, hệ thống xác nhận đơn và trừ cả OnHand/Reserved nguyên tử. Hủy/hết hạn chỉ giảm Reserved. Capture đến muộn vẫn được ghi nhận nhưng tạo yêu cầu hoàn tiền, không khôi phục đơn. Outbox/inbox, báo cáo và thông báo xử lý sau, có thể lặp delivery nhưng không được lặp hiệu ứng bền vững.

1. **Vì sao dùng modular monolith và một DbContext?** Hiểu ranh giới sở hữu module và lý do transaction chung giúp bảo vệ đơn/tồn kho; DbContext chỉ thuộc một unit of work, không chia sẻ cho các tác vụ đồng thời.
2. **Idempotency khác lease thế nào?** Lease phân công công việc tạm thời; unique identity và transaction bảo vệ hiệu ứng khi lease hết hạn hoặc tiến trình crash. CreateOrder phải replay phản hồi gốc, không dựng lại từ trạng thái hiện tại.
3. **Capture đúng lúc hết hạn xử lý ra sao?** `now >= ExpiresAt` là hết hạn tại quyết định sau khi khóa đơn; ghi capture, giải phóng reservation nếu cần và lên lịch hoàn tiền. Hiểu vì sao không được tin thời gian frontend/provider để hồi sinh đơn.

Before milestone 02, be able to explain the state tables, reservation arithmetic, strong/eventual consistency boundary, independent deduplication IDs, and why the pending runtime criteria are not yet test evidence.

Suggested commit title: `docs: establish OrderFlow implementation contract` (no title was supplied with the milestone; this is the proposed title). No commit was created.
