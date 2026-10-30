# OrderFlow implementation status

Updated: 2026-10-30 (Asia/Ho_Chi_Minh). Current milestone: **02 — initial solution structure**. The .NET solution and independently runnable API/worker composition are implemented. No frontend, authentication, database migrations, catalog, or order behavior exists yet. A01's .NET portion is complete; its client/proxy/database composition obligations remain for later milestones.

## Milestone 01 repository inspection (historical)

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
| 02 | Scaffold API/Application/Worker and test projects; pin supported SDK/NuGet/tools; build | 01 | A01 | Complete: .NET composition portion |
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

## Milestone 01 manual review and handoff (historical)

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

## Milestone 01 explanation and interview preparation (historical)

Milestone 01 thiết lập hợp đồng triển khai, chưa có chức năng chạy được. API và worker là hai tiến trình dùng chung một application project và một PostgreSQL. Mỗi module sở hữu schema riêng; một unit of work có thể gọi các module để cập nhật đơn, tồn kho, lịch sử và outbox trong cùng transaction.

Luồng chính dự kiến: tạo đơn kiểm tra người dùng và idempotency key, lấy giá từ server, giữ tồn kho đúng 15 phút rồi lưu phản hồi 201 gốc. Thanh toán đi qua ý định đã lưu, worker gọi gateway ngoài transaction. Khi xử lý capture hợp lệ trước hạn, hệ thống xác nhận đơn và trừ cả OnHand/Reserved nguyên tử. Hủy/hết hạn chỉ giảm Reserved. Capture đến muộn vẫn được ghi nhận nhưng tạo yêu cầu hoàn tiền, không khôi phục đơn. Outbox/inbox, báo cáo và thông báo xử lý sau, có thể lặp delivery nhưng không được lặp hiệu ứng bền vững.

1. **Vì sao dùng modular monolith và một DbContext?** Hiểu ranh giới sở hữu module và lý do transaction chung giúp bảo vệ đơn/tồn kho; DbContext chỉ thuộc một unit of work, không chia sẻ cho các tác vụ đồng thời.
2. **Idempotency khác lease thế nào?** Lease phân công công việc tạm thời; unique identity và transaction bảo vệ hiệu ứng khi lease hết hạn hoặc tiến trình crash. CreateOrder phải replay phản hồi gốc, không dựng lại từ trạng thái hiện tại.
3. **Capture đúng lúc hết hạn xử lý ra sao?** `now >= ExpiresAt` là hết hạn tại quyết định sau khi khóa đơn; ghi capture, giải phóng reservation nếu cần và lên lịch hoàn tiền. Hiểu vì sao không được tin thời gian frontend/provider để hồi sinh đơn.

Before milestone 02, be able to explain the state tables, reservation arithmetic, strong/eventual consistency boundary, independent deduplication IDs, and why the pending runtime criteria are not yet test evidence.

Suggested commit title: `docs: establish OrderFlow implementation contract` (no title was supplied with the milestone; this is the proposed title). No commit was created.

## Milestone 02 — initial solution structure

### Inspection and scope

Read root AGENTS.md, the project specification, architecture, milestone status, and ADRs before editing. The repository contained documentation only: no reusable solution/source/test projects, package manifests/versions/lockfiles, or migrations. Unlike the milestone-01 starting point, Git is now valid: `git status --short --branch` showed `main...origin/main` and no working-tree changes. Existing AGENTS.md/specification and earlier ADR decisions were preserved. No commit, push, deployment or history rewrite was performed.

The request's domain/application/infrastructure boundaries are implemented as folders inside the one Application assembly chosen by ADR-001, not three additional production projects. This aligns with the contract; no behavioral contradiction or scope change was necessary. [ADR-007](adr/0007-solution-bootstrap.md) records the bootstrap details. No authentication, EF/Npgsql, DbContext, migration, catalog/order behavior, frontend, or future background job was introduced.

### Completed acceptance

| Milestone-02 requirement | Result / evidence |
| --- | --- |
| Compatible stable SDK, pinned dependencies and repeatable restore | Complete: SDK 10.0.114, net10.0, five central package pins and five generated package lockfiles; cold restore and fresh-output locked restore passed |
| API/worker/application plus unit/integration test projects | Complete: five projects in OrderFlow.slnx; acyclic references verified; Application has no external dependency or placeholder business code |
| API starts with basic liveness | Complete: WebApplicationFactory smoke test plus separate Kestrel launch, HTTP 200 `{"status":"Healthy"}`, and SIGTERM exit 0 |
| Worker starts/stops without fake future work | Complete: Generic Host with no registered jobs; StartAsync/StopAsync smoke test plus separate terminal launch and Ctrl+C exit 0 |
| External-service-free composition smoke tests | Complete: 2 passed, 0 failed/skipped; UnitTests intentionally has zero domain tests |
| Editor/ignore/sample configuration/setup commands | Complete: root tooling files, safe per-host JSON logging/host settings, .env.example and README configuration guidance |
| No generated output or secret-bearing configuration in deliverables | Complete: artifacts/CLI state/NuGet caches used for verification are under /tmp; file/config/ignore checks passed; no private .env/certificate/connection-string/provider-secret file added |
| A01 overall | Partial: the .NET portion is complete; client, PostgreSQL local setup, reverse proxy and full deployment composition remain milestones 03/04/25 |

Resolved direct versions: Microsoft.Extensions.Hosting **10.0.14**, Microsoft.AspNetCore.Mvc.Testing **10.0.14**, Microsoft.NET.Test.Sdk **17.14.1**, xUnit **2.9.3**, xunit.runner.visualstudio **3.1.4**. The .NET SDK and Microsoft package servicing versions were checked against official release/package documentation; none are previews. Transitive versions/content hashes come from NuGet-generated lockfiles, not handwritten guesses. No extra CLI tool was needed. NuGet auditing remained enabled for all dependencies and restore reported no warnings.

### Important files and migrations

- [Solution](../OrderFlow.slnx), [global.json](../global.json), [Directory.Build.props](../Directory.Build.props), [Directory.Packages.props](../Directory.Packages.props), [NuGet.config](../NuGet.config).
- [API entry point](../src/OrderFlow.Api/Program.cs), [worker entry point](../src/OrderFlow.Worker/Program.cs), [worker composition](../src/OrderFlow.Worker/WorkerHost.cs), and both hosts' appsettings.json.
- [Application module boundaries](../src/OrderFlow.Application/Modules/README.md), Persistence/Messaging boundary READMEs, and [Application project](../src/OrderFlow.Application/OrderFlow.Application.csproj).
- [Composition smoke tests](../tests/OrderFlow.IntegrationTests/CompositionSmokeTests.cs), both test project files, and one generated packages.lock.json per project.
- [.editorconfig](../.editorconfig), [.gitignore](../.gitignore), [.env.example](../.env.example), [README](../README.md), updated architecture/status and ADR index, new ADR-007.
- **New database migrations: none.** PostgreSQL/context/migration setup remains milestone 03.

### Exact execution and results

In addition to file/ancestor-instruction/Git inspection, ran `dotnet --info`, SDK/runtime/targeting-pack inspection, official package/release documentation checks, `dotnet new sln -n OrderFlow --format slnx`, and `dotnet sln OrderFlow.slnx add` for the five projects. Git's valid/clean state was checked before edits.

Verification used these non-secret tooling environment overrides (no system HOME change):

```bash
export DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES=/tmp/orderflow-m02/packages
export NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache
```

1. `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-m02/artifacts` — exit 0, all five projects restored from the official feed into an initially empty temporary package cache; generated five lockfiles.
2. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-m02/artifacts --verbosity normal -m:1 -nr:false` — exit 0, 0 warnings/errors.
3. `dotnet build OrderFlow.slnx --configuration Release --no-restore --artifacts-path /tmp/orderflow-m02/artifacts -m:1 -nr:false` — exit 0, all five projects, 0 warnings/errors.
4. `dotnet test OrderFlow.slnx --configuration Release --no-build --no-restore --artifacts-path /tmp/orderflow-m02/artifacts -m:1 -nr:false` — exit 0 with suitable local-socket permission; 2 composition tests passed; UnitTests reports no tests, intentionally.
5. Fresh output directory, standard commands without the single-node flags: `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-m02/clean-artifacts`, then `dotnet build OrderFlow.slnx --configuration Release --no-restore --artifacts-path /tmp/orderflow-m02/clean-artifacts`, then `dotnet test OrderFlow.slnx --configuration Release --no-build --no-restore --artifacts-path /tmp/orderflow-m02/clean-artifacts` — all exit 0; build 0 warnings/errors, 2 tests passed. This verifies the documented solution commands against a clean artifacts directory.
6. `dotnet run --project src/OrderFlow.Api --configuration Release --no-build --no-restore --no-launch-profile --artifacts-path /tmp/orderflow-m02/artifacts -- --urls http://127.0.0.1:5080` — Kestrel startup observed. `curl --fail --silent --show-error -i --max-time 10 http://127.0.0.1:5080/api/health/live` returned 200 JSON. Sent SIGTERM only to that verification instance; shutdown log observed and launcher exited 0.
7. `dotnet run --project src/OrderFlow.Worker --configuration Release --no-build --no-restore --no-launch-profile --artifacts-path /tmp/orderflow-m02/artifacts` — standalone Production startup observed, JSON console logs with UTC timestamp. Sent terminal Ctrl+C; graceful shutdown log observed and exit 0. Both verification hosts were stopped.
8. Python file/config/link/XML/project-graph checks — passed: five acyclic projects, SDK/central versions matching generated locks, no build/certificate/private configuration files, safe sample JSON keys, valid local documentation links and formatting. `git diff --check` passed. `git check-ignore --no-index` confirmed build output/private .env/local settings/certificates are ignored, while .env.example/appsettings.json/lockfiles remain includable.

Environment restrictions encountered and resolved: first CLI template discovery could not write the default read-only `.dotnet` sentinel; using DOTNET_CLI_HOME under /tmp fixed it. Sandbox DNS could not resolve api.nuget.org; authorized network access restored packages successfully. A sandbox default parallel locked restore exited 1 without diagnostic text; single-node restore passed and the standard command later passed outside the sandbox restriction. Sandbox VSTest initially aborted with SocketException (13), Permission denied, before executing tests; rerunning with loopback/socket permission passed. These initial failures are not claimed as passes, and no approval review rejection remains outstanding.

Unverified limits: no PostgreSQL/SQL/authorization/transaction/race/browser/client tests are possible or claimed for this scaffold. Composition tests use actual host construction but do not prove future business behavior. Linux x64/.NET 10 was verified; Windows/macOS were not executed. Liveness is not readiness, and no production proxy/HTTPS deployment was tested. Configuration/secret inspection is scoped to the files introduced here, not a security certification.

### Manual verification and trade-offs

Follow [README setup](../README.md): install SDK 10.0.114, run solution locked restore/Release build/test, start API and worker in different terminals, request `/api/health/live`, and press Ctrl+C to stop each host. For external artifacts, use the exact paths above consistently across restore/build/test/run. Confirm the worker logs lifecycle only, and the repository contains no generated bin/obj/artifacts output or real credentials. No database preparation is needed yet.

Trade-offs: three production projects keep the portfolio easy to explain, while folder boundaries rely on review rather than compile-time layer isolation. Exact SDK selection requires an explicit update when servicing changes. An empty unit-test project reports no tests instead of supplying meaningless passing tests. An idle worker proves lifecycle/composition only; background correctness begins with real jobs. Generated dependency lockfiles are deliberately retained as source-controlled build inputs.

### Vietnamese explanation and interview preparation

Milestone 02 đã tạo solution gồm API, Worker, Application và hai test projects. `Program` của API dựng WebApplication rồi map GET `/api/health/live`; endpoint chỉ nói tiến trình API đang sống. Worker gọi `WorkerHost.Build` để dựng Generic Host và `RunAsync` để chờ tín hiệu dừng; chưa có BackgroundService hoặc vòng lặp giả lập công việc. API/Worker cùng tham chiếu Application, còn Application không tham chiếu ngược lại host nên không có dependency vòng.

Domain rules và use case sẽ nằm theo module/feature trong Application; persistence và messaging là các ranh giới hạ tầng cùng assembly theo ADR-001. Hiện tại các ranh giới này chỉ có tài liệu, chưa có nghiệp vụ hay EF context. SDK được chọn qua global.json, version package qua Directory.Packages.props và graph thực tế qua packages.lock.json. Smoke test dựng API thật qua TestServer và kiểm tra vòng đời worker thật, không cần database/dịch vụ ngoài.

1. **SDK pin khác package lock thế nào?** Hiểu global.json chọn bộ công cụ, central versions khai báo dependency trực tiếp, lockfile cố định graph/hash; locked restore phát hiện graph lệch.
2. **Vì sao liveness không kiểm tra PostgreSQL?** Hiểu liveness mô tả tiến trình, readiness mô tả khả năng phục vụ phụ thuộc; scaffold chưa có database nên không được giả vờ báo database sẵn sàng.
3. **Worker chưa có job thì chạy gì?** Hiểu Generic Host quản lý DI/config/logging/vòng đời và tín hiệu shutdown; sau này mỗi job phải tạo scope riêng, không chia sẻ DbContext giữa tác vụ đồng thời.

Before proceeding, explain the five-project reference graph, folder-vs-project boundary choice, host lifecycle, real scope of smoke tests, and reproducible dependency workflow. Remaining work is milestones 03–29, the rest of A01, and A02–A24. Next: PostgreSQL/context/initial migration and integration harness, without jumping into authentication/catalog/orders.

Suggested commit title: `chore: initialize OrderFlow solution structure` (the milestone supplied no explicit title). No commit was created.
