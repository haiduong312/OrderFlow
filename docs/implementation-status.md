# OrderFlow implementation status

Updated: 2026-11-16 (Asia/Ho_Chi_Minh). Current milestone: **20 customer browsing and cart**. URL-scoped public catalog list/detail/filter/sort/pagination, abortable queries, a bounded immutable browser-local Zustand cart, exact estimated totals and price/stock rechecks are implemented under ADR-028. The cart is explicitly shared across sign-ins; private auth/order queries retain ADR-027 isolation. Actual checkout/recovery/payment/Admin/report/notification UI and production proxy remain deferred. See the final milestone section for checks and limitations.

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
| 03 | PostgreSQL/Identity storage, reliability schema, initial migration, Compose and actual database tests | 02, 02a | A01, A16 | Complete: persistence foundations |
| 04 | Customer browser auth endpoints/cookies, required CSRF and safe Admin bootstrap using existing Identity storage | 03 | A02 | Complete: auth foundation; ownership features remain later |
| 05 | React/TypeScript workspace; pinned Vite/Node tooling, scoped TanStack Query, UI-only Zustand and same-origin dev proxy | 02, 02a, 04 auth | A01, A02 current slice | Complete for frontend foundations under ADR-027; production proxy/deployment remain 25/26 |
| 06 | Authorization/ownership contracts/policies/handlers, denial mapping and focused CSRF/auth/resource tests | 04 | A02 | Complete: representative-resource foundations; real Order integration remains 13/19 |
| 07 | Products/Categories domain/schema, Admin create/update/archive, public read/search/paging, exact prices and concurrency | 03, 06 | A03 | Complete: Catalog slice; Order snapshots/totals remain 11/12 |
| 08 | Inventory balances/append-only movements, zero initialization/backfill, Admin receipts/adjustments/replay and stock races | 07 | A04, A16 | Complete: stock foundation; reservations deferred to 09 |
| 08a | Reusable PostgreSQL/WebApplicationFactory harness, per-case isolation, logical time and safe race boundaries | 08 | A02, A04, A16, A23 (current slices) | Complete: 238 tests; PostgreSQL subset repeated; no new business feature |
| 09 | Reservation model/behavior and necessary Order identity/FK prerequisites; SQL/domain/race tests | 08 | A04, A07, A08, A16 | Active create/reserve delivered in 12; release/terminal guards in 13; consumption delivered with signed settlement in 17/18 |
| 10 | Operation-specific normalization, replay/conflict protocol using the shared idempotency store | 03, 06 | A06 | CreateOrder:v1 normalization/replay/conflict delivered with 12; later operations retain separate identities |
| 11 | Complete Order/item snapshots/lifecycle and transactional events on reservation/Order foundations | 07, 09 | A03, A07, A08 | Creation delivered in 12; cancellation/expiry and fulfillment/history in 13; verified confirmation delivered in 17/18 |
| 12 | Atomic CreateOrder/reserve + original response; stock/idempotency concurrency and rollback | 08a; checkout portions of 09–11 | A05, A06, A08 creation/replay, A16, A23 | Complete for requested checkout; see evidence and remaining lifecycle work below |
| 13 | Own order reads/list, pending cancellation/release, requested Admin fulfillment and authorization/race regressions | 12 | A02, A07 release, A08, A09, A15 graph | Complete for requested scope; automatic expiry and payment/capture integration remain later |
| 14 | Outbox dispatch/inbox and worker lease/retry foundation; expiry worker + deterministic races | 13 | A07–A09, A16–A19 | Expiry and RabbitMQ publisher slices complete: bounded SKIP LOCKED claims, token leases, confirms/mandatory routing and finite durable retry/DLQ; business-consumer inbox/effects remain 23/24 |
| 15 | Payment attempt/intention schema, request idempotency, active-attempt constraint, customer start/read | 10; expiry/worker foundations of 14 | A12, A13, A23 | Complete for requested initiation scope; verified settlement remains 17/18 |
| 16 | Durable gateway simulator, submission/query worker, independent transaction boundary and uncertainty | 15 | A12, A13, A19, A23 | Provider/submission complete; signed receipt/settlement in 17/18 and compensation delivery/lookup now implemented; automatic callback delivery remains later |
| 17 | Signed callback receipt/inbox, verification, duplicate/quarantine handling and replay tests | 16 | A13, A14 | Complete: signed bounded receipt, exact-byte identity/deduplication, durable inbox and quarantine; see ADR-019 |
| 18 | Atomic capture confirmation/stock consumption; late capture compensation/refund reconciliation and races | 17 | A07–A13, A16, A17, A19, A23 | Complete for requested late-capture recovery: shared allocation/consumption, full-compensation delivery/lookup, bounded review and provider-dependent recovery; operator replay/real-provider hardening remain future |
| 19 | Admin fulfillment state machine, expected versions, order inspection and authorization | 13; verified payment integration 18 | A02, A15 | Requested list/detail/graph/expectedVersion APIs delivered in 13; verified payment-confirmed end-to-end integration remains 18/19 |
| 20 | Browser session/CSRF flow, active catalog, cart and exact cents state | 05, 06, 07 | A03, A20 | Complete for milestone 20 under ADR-027/028: session, browsing/detail/filter/paging, bounded browser cart, exact cents and advisory revalidation; checkout remains 21 |
| 21 | Checkout UI, pending key/body recovery, authoritative order/deadline/payment/cancel views | 18, 20 | A03, A20 | Read-only scoped order-list prerequisite delivered with ADR-027; checkout/detail/actions/recovery UI remain pending |
| 22 | Admin catalog/stock/order/fulfillment UI with state/version conflict handling | 09, 19, 20 | A20 | Pending |
| 23 | Deduplicated local notifications consumer, durable deliveries and failure visibility | 14, 18 | A17, A18, A21 | Complete for notification/email/realtime backend slices: durable consumers/owner APIs, leased local SMTP/admin recovery and API SignalR/Redis hints; frontend integration remains a later 23 slice, production mail/proxy/general replay later |
| 24 | Direct bounded Admin reports/audit, explicit financial semantics, UTC/money/auth/snapshot tests and measured read indexes; report UI later | 08a, 18, 19 backend | A02, A03, A16 read, A21, A23 | Backend complete under ADR-026; no reporting event projection/consumer; report UI remains pending on 20 |
| 25 | Reproducible API/client/worker/proxy local composition, migration command, runtime DB roles and readiness | 22, 24 | A01, A16, A22 | Pending |
| 26 | Deployment hardening/shared protected Data Protection keys; SignalR proxy affinity/Upgrade/trusted forwarded headers; multi-worker/crash/uncertain commit/stale lease fault suite; retry/no-network-in-transaction evidence | 04, 18, 23, 24, 25 | A02, A12, A17–A19, A22, A23 | Pending |
| 27 | Browser acceptance flows; cross-user/CSRF/auth regressions; structured redaction/config checks | 21, 22, 25, 26 | A02, A20, A22 | Auth/session/proxy/basic responsive flows covered with ADR-027; full business/deployment acceptance remains pending |
| 28 | Operational backlog/failure view, health/lag metrics, audited same-identity operator replay and recovery runbook | 25, 26, 27 | A19, A22 | Pending |
| 29 | Full acceptance walkthrough; portfolio architecture/demo/interview guide and verified limitations | 28 | A01–A24 | Pending |

Feature order is intentional: HTTP idempotency/stock correctness precede payment; outbox/worker durability precede external intentions; auth/CSRF precede protected client flows; fault tests and operability precede the final demo. Per the milestone-08 request, full reservations and their required Order identity/FK prerequisites move to 09; 11 completes Order lines/lifecycle/events; command-to-order links can be completed in 12 after both schemas exist. Reliability storage is now implemented in 03, ahead of its operation-specific use in 10/11/12 and dispatch in 14. Required CSRF protection ships with auth endpoints in 04; 06 extends authorization/regression coverage. Each feature milestone writes its relevant tests then; 26 does not defer concurrency proof. The latest instruction swaps the prior 04 React/05 Identity deliverables to 05/04; ADR-009 records that scope correction. The milestone-04 request additionally assigns shared Data Protection key implementation to 26; ADR-010 extends that milestone with deployment hardening while retaining its fault suite. Milestone 25 remains local composition. The next explicit request implements authorization milestone 06 before React 05; its dependency is 04, so no prerequisite or roadmap ID is skipped implicitly. ADR-011 records this delivery order. ADR-013 moves Admin stock APIs/replay/race work from 09 into 08 and full reservations/required Order prerequisites into 09; it retains checkout, expiry-worker and payment integration in later milestones.

Requested supplemental milestone **02a** depends on 02 and is **Complete: shared HTTP/application foundations**. It establishes the common error/JSON/time/pagination conventions used by 03 and later HTTP features. IDs 01–29 and their business scopes are retained; 03 remains PostgreSQL work, not implicitly completed by this addition. ADR-008 records this roadmap adjustment and the clarified money input/output grammar.

Requested checkout milestone **12** now pulls only the necessary creation portions of 09/10/11 and owner detail read from 13 into its deliverable. Milestone IDs remain stable; 09 retains consume/release/terminal reservation rules, 11 later Order lifecycle, 13 listing/cancellation, 14 automatic expiry/dispatch and 18 capture allocation. Milestone 12 depends on the complete stock/harness foundations and its implemented creation prerequisites, not on pretending the full future lifecycle is already complete. ADR-015 records this scope correction and supersedes the original duplicate-merging behavior with explicit duplicate rejection.

Requested order-management milestone **13** also pulls Admin inspection/fulfillment endpoints from 19 forward, without implementing payment confirmation or manufacturing a paid-order seed endpoint. The release portion of 09 and status-history/lifecycle portion of 11 now ship here. All existing-order mutations use the same PostgreSQL order-first coordination and fixed expiry boundary; read-only list/detail views never bulk-expire rows. ADR-016 records these scope/name changes and the data-preserving OrderHistory -> OrderStatusHistory migration.

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

## Milestone 02a — shared HTTP/application foundations

### Scope, inspection and completed acceptance

Before editing, read AGENTS.md, specification, architecture, implementation status, ADRs, existing source/tests/project files/central package versions/lockfiles, and Git status. Git was valid/clean on main at inspection. Reused all five projects, SDK 10.0.114 and existing package versions; no migrations existed. No unrelated work was changed or commit/push/deployment performed.

Reported the roadmap mismatch before implementation: the user requested HTTP/application foundations while numbered 03 was PostgreSQL. Added 02a without renumbering 01–29 or implementing database prerequisites. Also clarified money input accepting at most two fractional digits while preserving two-place output, in ADR-008/specification.

| Requirement | Completed behavior / evidence |
| --- | --- |
| Consistent errors and explicit business mapping | ApiProblemDetails contains type/title/status/code/traceId/errors; eight expected application mappings plus documented bodyless HTTP statuses; unclassified failures remain logged sanitized 500 |
| Validation and privacy | MVC binding/DataAnnotations failures return 400 with safe camelCase/path field errors; malformed JSON, unknown JSON properties, invalid scale/range/tokens and pagination tested; not_found never includes owner/resource details |
| Trace and structured request/failure logs | Same body/header/log trace, W3C traceparent linkage, untrusted X-Trace-Id ignored; route templates preserved after exception handling; sensitive test body/query/cookie/auth/exception markers absent from captured logs/responses |
| Cancellation | Real client cancellation after deterministic action-entry gate is logged Aborted/diagnostic 499, without business/500 Error; non-request cancellation is a sanitized 500 |
| Time and money primitives | Both hosts register TimeProvider; UTC/microsecond truncation and inclusive expiry tested using a manual clock; typed UsdMoney accepts invariant 0–2-place strings, writes 2 places, rejects extra scale/range, leaves ordinary numbers numeric |
| Pagination and OpenAPI | Validated defaults 1/20, size <=100, overflow guarded; deterministic sorting conventions documented; first-party OpenAPI 10.0.14, generated 3.1.1 document in Development only, money/error schemas verified |
| Examples and scope | HTTP contract includes real liveness/404 and test-probe validation/money response examples; production does not expose test probes or CRUD; no database migration |

A03 is partial (money primitive/HTTP JSON only; positive-price/domain totals and PostgreSQL constraints pending). A08 is partial (generic boundary/clock precision only; actual reservations/deadlines pending). A22 is partial (safe HTTP/request/failure logs only; operational monitoring/readiness remains pending). A02 ownership/Identity/antiforgery and all SQL transaction/race criteria remain unimplemented; a safe common 404 mapping does not prove an ownership query. No additional acceptance criterion is claimed complete merely from a synthetic error probe.

### Important files, packages and migrations

- Application/Common: Errors/ApplicationError.cs and ApplicationFailureException.cs; Money/UsdMoney.cs; Time/BusinessTime.cs; Pagination/PageRequest.cs. Application still has no external package/HTTP dependency.
- API/Http: HttpFoundations registration/pipeline, ApiProblemDetails, ApiProblems, ApiExceptionHandler, RequestLoggingMiddleware and UsdMoneyJsonConverter. Program wires these and typed liveness metadata; WorkerHost registers the same TimeProvider abstraction.
- Tests: UsdMoneyTests, BusinessTimeTests, PageRequestTests; HTTP test factory/controller/capture logger and HttpFoundationTests; original two composition tests retained.
- [HTTP contract](http-contract.md), [ADR-008](adr/0008-http-application-foundations.md), updated README/architecture/specification/ADR index and this status.
- One new direct package: Microsoft.AspNetCore.OpenApi **10.0.14**, centrally pinned. NuGet resolved Microsoft.OpenApi **2.12.2** transitively; API/integration lockfiles updated by restore. Other direct versions/SDK unchanged. **Migrations: none.**

### Execution, results and limitations

Tooling environment used DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, NUGET_PACKAGES=/tmp/orderflow-m02/packages, and NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache for restore. All build/test results are outside the repository under /tmp/orderflow-foundations. Network/loopback permission was required for NuGet/VSTest as in milestone 02; no approval rejection remained outstanding.

Commands/results:

1. `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-foundations/artifacts` — exit 0, official package resolved and lockfiles generated, no preview/audit bypass.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-foundations/artifacts -m:1 -nr:false` — final exit 0, all five projects, **0 warnings/errors**.
3. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-foundations/artifacts -m:1 -nr:false --logger 'trx;LogFilePrefix=foundations' --results-directory /tmp/orderflow-foundations/test-results` — final exit 0: **37 unit + 49 integration/composition = 86 passed**, 0 failed/skipped. Final result files: foundations_net10.0_20261030160500.trx (integration), foundations_net10.0_20261030160501.trx (unit), under that results directory.
4. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-foundations/artifacts -m:1 -nr:false` — exit 0, all five projects, no warnings/audit bypass. `git diff --check` passed. Final Python inventory/link/config/XML/reference/lock verification passed: 61 source/config/document files, 65 valid local file/anchor links, five acyclic projects, six central versions matching lockfiles, no generated build/certificate/private configuration files. Tracked host settings contain only safe logging/AllowedHosts keys.

5. Launched the API separately with `DOTNET_ENVIRONMENT=Development dotnet run --project src/OrderFlow.Api -c Release --no-build --no-restore --no-launch-profile --artifacts-path /tmp/orderflow-foundations/artifacts -- --urls http://127.0.0.1:5080` (same tooling overrides). Actual curl checks returned 200 liveness, 404 application/problem+json even with Accept:text/html, and OpenAPI 3.1.1 containing only liveness plus the shared error schema (no probe/CRUD routes). Checked matching trace headers/body/logs and template-only JSON log output using synthetic sensitive path/query markers. Ctrl+C shutdown exited 0; verification hosts were stopped.

Initial build attempts caught a WriteAsJsonAsync overload mismatch and an xUnit2031 analyzer error; both corrected without suppressions using the installed reference signature/preferred assertion. A premature no-build test invocation could run only UnitTests before the integration assembly existed and exited 1; it is not claimed as a passing suite. The first complete test run caught two real issues: nullable money schema metadata and original route template loss after exception handling. Both were fixed. Actual Kestrel console inspection then revealed native framework scopes included raw RequestPath despite safe event fields. Disabled API JSON IncludeScopes and added a regression through the actual configured JSON formatter with synthetic sensitive scope markers; final full suite passed all 86 tests. Trace remains an explicit event field. Tests assert actual body/status/header/log fields and cancellation progression, not arbitrary sleeps.

There is no SQL/EF InMemory/SQLite test or actual database provider in this milestone. A synthetic DbException tests HTTP classification only, not SQL behavior. No authentication, ownership query, antiforgery, CRUD, price arithmetic, reservation, collection query or job is implemented. Pagination sorting remains a documented SQL convention until queries exist. Current production DTOs have no timestamp field; future timestamp JSON must still emit specification-compliant Z strings. Native pre-pipeline/response-started transport failures cannot be promised a fresh complete ProblemDetails body. Windows/macOS and proxy/HTTPS deployment were not exercised.

9. After strengthening only the HTTP assertions, `dotnet test tests/OrderFlow.IntegrationTests -c Release --no-restore --artifacts-path /tmp/orderflow-expiry/artifacts --filter 'FullyQualifiedName~Deadline_uses_injected_UTC_microsecond_boundary' --logger 'trx;LogFilePrefix=expiry-http-boundary' --results-directory /tmp/orderflow-expiry/results` — exit 0, **3/3 passed**. Verifies current HTTP status/version/effective expiry and System/null actor JSON. TRX expiry-http-boundary_net10.0_20261106151918.trx. No production code changed after the full suite.

### Manual verification, trade-offs and interview preparation

Run the README locked restore/Release build/test commands. Launch the API and request `/api/health/live`, then `/api/does-not-exist` with Accept:text/html: expect 200 JSON and generic 404 problem JSON, both with X-Trace-Id. Set DOTNET_ENVIRONMENT=Development and restart to inspect `/openapi/v1.json`; Production does not expose it. Validation/money/forced-error routes are test-only, so exercise them through tests rather than deploying probes. Exact HTTP response examples are in http-contract.md. No database setup is needed for these checks.

Luồng chính: Program đăng ký các nền tảng rồi đưa request qua trace/logging, exception handler và status-code handling. MVC kiểm tra JSON/model trước action; lỗi application được map rõ ràng. Lỗi bất ngờ trả 500 với mã internal_error, server log type/code/trace và không log raw exception có thể chứa dữ liệu nhạy cảm. UsdMoney giữ decimal chính xác và converter chỉ đổi kiểu tiền thành chuỗi; quantity/ratio vẫn là số. TimeProvider được inject, helper chuẩn hóa UTC/microsecond và so sánh hết hạn bằng >=. Worker chưa có job.

Trade-offs: simple expected-failure exceptions avoid an unnecessary framework; generic binding messages protect values but are less descriptive. Redacting raw exception text trades some immediate diagnostics for privacy. UsdMoney allows zero for reports, so future price rules must separately require >0. Folder boundaries and future SQL sorting still need review/testing; current tests do not replace actual PostgreSQL tests.

1. **Vì sao không convert toàn bộ decimal sang string?** Hiểu typed money converter giữ hợp đồng tiền mà không làm đổi các số không phải tiền; parser phải kiểm tra scale trước khi database có thể làm tròn.
2. **Khi nào cancellation là 500?** Hiểu RequestAborted chứng minh client đã hủy; OperationCanceledException khi request còn hoạt động không được tự coi là thành công hay lỗi business.
3. **Vì sao lỗi ownership dùng 404?** Hiểu query phải lọc theo owner, và cùng một not_found che việc resource tồn tại; mapping chung chưa phải bằng chứng authorization đã được triển khai.

Before proceeding, explain the pipeline order, validation/error/log trace flow, known-vs-unknown failure classification, money input/output/zero rules, clock precision, pagination overflow, and scope of HTTP-only tests. Milestones 03–29 remain pending.

Suggested commit title: `feat: add shared HTTP and application foundations` (no explicit title was supplied). No commit was created.

## Milestone 03 — PostgreSQL persistence and durable reliability storage

### Inspection and scope decisions

Read AGENTS.md, specification, architecture, status and ADRs; inspected all five projects, existing tests, SDK/central versions/lockfiles and migration absence. `git status --short --branch` was clean on main at start. Reused the existing solution and SDK. No unrelated work, commit, push, deployment or user data deletion was performed.

Reported two scope differences before implementation: latest request places auth endpoints at 04 (previously 05) and asks for shared reliability storage now (previous storage prerequisites were spread over 10/11/14). Roadmap now assigns 04 browser auth with required CSRF, 05 React, 06 further authorization/regression work; 10/11/14 retain their operation/event/dispatch behavior on the shared schema. ADR-009 records this, plus the generalization of CreateOrderRequests/PaymentRequests into IdempotencyRecords. No auth endpoint, fake user, catalog/order/payment entity, publisher or background lease processor was implemented.

### Completed milestone acceptance and evidence

| Requirement | Result / evidence |
| --- | --- |
| Actual PostgreSQL and initial migration | Complete: PostgreSQL 18.6-bookworm pinned tag/digest; clean database migrated with 20261102023642_InitialPersistence; second migrate no-op; model drift check clean; idempotent SQL script generated |
| Scoped shared context and Identity storage | Complete: API/worker share registration, separate context per scope; actual connections and UserManager/IUserStore persistence verified; explicit Identity Guid/table/schema mappings, standard indexes/FKs; no cookies/auth endpoints |
| IdempotencyRecords | Complete storage only: user/operation/key unique index, C collation, restrictive User FK, canonical text/SHA-256, original successful byte response/status/type/Location; state/time/hash/key CHECKs and immutable-request/completed-snapshot trigger |
| OutboxMessages / ProcessedMessages | Complete storage only: independent event and consumer/message identities, envelope/version/payload fields, publish/process states, attempts/due/owner/token/expiry/terminal fields, CHECKs and queue indexes; inbox does not require a local outbox event |
| Shared atomic unit of work | Complete foundation proof: Identity row change + request snapshot + outbox + inbox commit or roll back in one PostgreSQL transaction; no independent repository/store commits |
| Precision/time | Complete conventions: UTC/microsecond DateTimeOffset converter, numeric(18,2)/UsdMoney conversion; actual offset/precision roundtrip and test-only temporary money-table maximum-value roundtrip |
| Compose/data lifetime | Complete configuration/health/data checks: PostgreSQL-only dev profile, loopback port, file-backed private secret, named volume at PG18 parent directory, health check; actual Compose down/up without --volumes retained migration/two seeded roles |
| Dedicated migration and seed | Complete: no startup migration/EnsureCreated/reset; explicit Development-only roles seed rerun twice, two roles/zero accounts; guide covers inspection, preserving old data via new project, and manual destructive reset only |

These are foundations, not full A06/A16–A19: normalization/replay HTTP conflicts, actual order/stock/history atomicity, worker claim/fencing/retry/delivery and consumer-effect protocols remain pending. A02 authentication/ownership is pending. A01 still needs client/proxy composition; A03 still needs real positive-price/entity constraints and authoritative totals. Runtime least-privilege roles/history permissions and production deployment remain later operational work.

### Files, versions and migration

- [Compose](../compose.yaml), [.env.example](../.env.example), [.gitignore](../.gitignore) and [local EF tool manifest](../.config/dotnet-tools.json).
- [OrderFlowDbContext/converters](../src/OrderFlow.Application/Persistence/OrderFlowDbContext.cs), [registration](../src/OrderFlow.Application/Persistence/PersistenceRegistration.cs), [design-time factory](../src/OrderFlow.Application/Persistence/OrderFlowDesignTimeFactory.cs), and [ApplicationUser](../src/OrderFlow.Application/Modules/Identity/ApplicationUser.cs).
- IdempotencyRecord + explicit configuration; OutboxMessage/ProcessedMessage + explicit configurations; [explicit role seed](../src/OrderFlow.Application/Persistence/DevelopmentSeeder.cs); API/worker composition wiring and Worker seed command.
- New migration [20261102023642_InitialPersistence](../src/OrderFlow.Application/Persistence/Migrations/20261102023642_InitialPersistence.cs), designer and model snapshot. Ten application tables (seven Identity plus three reliability) and the messaging migration-history table; no future module tables.
- [PostgreSQL fixture](../tests/OrderFlow.IntegrationTests/Persistence/PostgresDatabase.cs), [10 PostgreSQL tests](../tests/OrderFlow.IntegrationTests/Persistence/PersistenceTests.cs), [snapshot guard unit tests](../tests/OrderFlow.UnitTests/IdempotencyRecordTests.cs), updated generated package locks and central packages.
- [Persistence runbook](persistence.md), [ADR-009](adr/0009-postgresql-reliability-foundations.md), README/spec/architecture/roadmap updates.

New direct versions: EF Core/Design/Identity **10.0.14**, Npgsql.EntityFrameworkCore.PostgreSQL **10.0.3** (Npgsql **10.0.3** transitively); local dotnet-ef **10.0.14**. SDK/other direct package pins unchanged. Official release/API docs and actual package/tool metadata were consulted. Docker image `postgres:18.6-bookworm@sha256:5c4783a8cc3f0074912164f94dfa1be4e043438f0f707cf968d7af9a0d31a2c8` was pulled and version verified in psql. No preview, broker/testcontainer package or generic repository was added.

### Exact checks and results

Tooling used private/non-secret path overrides: DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache, DOTNET_CLI_TELEMETRY_OPTOUT=1. All build/test/tool SQL outputs are outside the repository under /tmp/orderflow-persistence. Private random verification credentials were read from a mode-0600 temporary file and passed through process environment; values were never printed or written to source/fixtures/logs/Git. ConnectionStrings__OrderFlow and ORDERFLOW_TEST_POSTGRES were set privately for their respective commands.

1. `dotnet tool restore` — exit 0, pinned dotnet-ef 10.0.14. `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-persistence/artifacts -m:1 -nr:false` and final same command with `--locked-mode` — exit 0, no warnings/audit bypass.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-persistence/artifacts -m:1 -nr:false` — final exit 0, all five projects, **0 warnings/errors**.
3. `dotnet ef migrations add InitialPersistence --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` — exit 0; reviewed generated SQL and added the explicit immutable-snapshot trigger/Down cleanup before first application. EF commands used ArtifactsPath=/tmp/orderflow-persistence/artifacts to match metadata/build output.
4. `dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` — exit 0 on empty orderflow DB. EF's initial absent-history probe logged a failed SELECT before creating/applying the first migration; final application/history succeeded. Repeated command exit 0, no migrations applied. `dotnet ef migrations has-pending-model-changes` with the same project/startup/configuration flags — exit 0, no changes. `dotnet ef migrations script --idempotent --output /tmp/orderflow-persistence/initial.sql` with the same flags — exit 0, reviewable SQL generated.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-persistence/artifacts -m:1 -nr:false --logger 'trx;LogFilePrefix=persistence' --results-directory /tmp/orderflow-persistence/test-results` with private ORDERFLOW_TEST_POSTGRES — exit 0: **59 integration/composition + 43 unit = 102 passed**, 0 failed/skipped. Final TRX files: persistence_net10.0_20261102100511.trx and persistence_net10.0_20261102100512.trx under that results directory. All SQL tests used the actual PostgreSQL provider and a newly generated/migrated database; no EF InMemory/SQLite substitute. The fixture dropped only its own generated test database afterward.
6. PostgreSQL-only Compose `config --quiet`, `up -d --wait --wait-timeout 90`, then `down` (no volumes flag), `up -d --wait` — health passed, migration and two roles survived container recreation; user count stayed zero. `psql` inspected server version, tables, indexes, constraints and snapshot trigger. Found Identity tables/standard indexes, 10 messaging indexes including history PK, 15 CHECKs, 7 FKs and the immutable-snapshot trigger. Ten application tables plus history were present.
7. `DOTNET_ENVIRONMENT=Development dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile --artifacts-path /tmp/orderflow-persistence/artifacts -- seed-development` — exit 0 twice, no duplicate roles/users/passwords; no missing migrations. Separate normal host scope connectivity/Identity stores are covered in the SQL test. Existing HTTP/composition tests still prove liveness and worker startup without external services.
8. After stopping/removing the verification container/network **without deleting the named volume**, the same solution test command with `--filter 'Category!=PostgreSQL'` and no test connection setting passed the external-service-free subset: 49 HTTP/composition + 43 unit = **92 passed**. Volume `orderflow-m03-check_postgres_data` remains retained. `git diff --check`, final file/link/JSON/XML/reference/lock/roadmap checks and TRX-counter verification passed: 82 repository files, 91 local links, five acyclic projects, ten central direct package pins, no generated/private output or verification credential in deliverables. SQL script includes the snapshot-protection trigger.

Docker environment limitation and workaround: `/snap/bin/docker` failed first on a read-only Snap runtime path, then on missing snap-confine cap_dac_override even with elevated execution. The actual `/snap/docker/current/bin/docker` and standalone `/snap/docker/current/usr/libexec/docker/cli-plugins/docker-compose` worked (Engine 29.3.1, Compose 5.1.1). The daemon has a separate filesystem and could not bind the session's /tmp password file. The committed file-secret Compose configuration passed validation, but that literal mount could not run in this environment. An **uncommitted /tmp verification-only override** removed the bind secret and injected the generated private value through environment; the same image/profile/health/port/volume were exercised successfully. A normal local daemon can use the committed profile directly. No approval review rejection remains outstanding.

Initial SQL test attempts correctly exposed test-assumption errors: deleting a user with its tracked restrictive dependent first hit EF's client-side guard; clearing tracked dependents let the test reach PostgreSQL, where ON DELETE RESTRICT returns SQLSTATE **23001**, unlike missing-FK insertion **23503**. Corrected the assertion to the exact restrict_violation code, without changing or weakening production FK behavior. Subsequent SQL/full runs passed. No failed attempt is claimed as a pass.

Unverified/deferred: literal local-secret bind mounting under this isolated daemon, Windows/macOS, production TLS/proxy/runtime DB privileges, migration Down execution on populated data, auth/ownership SQL/HTTP, business aggregates, idempotent HTTP replay, broker/consumer publishing, expired-lease fencing/claim races and external side effects. All of these require later evidence; schema/transaction tests alone do not claim exactly-once delivery.

### Manual verification, flow and interview preparation

Follow persistence.md: create private secret once -> start Compose dev DB -> export private connection -> restore local tool/build -> run dedicated EF update -> inspect tables -> optionally run roles-only seed twice -> set test administrator connection and run tests. Normal startup does not perform migration or seed. `down` keeps data; using a new project/port preserves old data while giving a fresh volume. Destructive `down --volumes` is documented for a deliberately selected disposable project only; no reset script or automatic wipe was added/executed. Verification data/volume are retained after stopping the verification project.

Luồng chính: API và worker đăng ký cùng một DbContext theo scope. Khi cần persistence, scope lấy cấu hình kết nối riêng và tạo context; không có database call/migrate lúc startup. Identity storage và ba bảng reliability nằm trong cùng context nên SaveChanges/transaction có thể ghi thay đổi, snapshot, outbox và inbox cùng nhau. Unique/FK/CHECK và trigger do PostgreSQL bảo vệ qua nhiều process. Converter chuẩn hóa UTC/microsecond và tiền numeric(18,2). Migration chạy riêng bằng EF CLI; seed chỉ là lệnh Development tường minh tạo hai role, không tạo tài khoản. Chưa có publisher/worker claim hay nghiệp vụ đơn hàng.

Trade-offs: shared schema/context simplify atomicity but couple releases; initial dev owner has broad privileges and must not become a production runtime account. Completed snapshots consume storage and remain immutable; canonicalization/replay still belong to future operation handlers. PostgreSQL trigger is intentionally provider-specific. No idempotency lease is needed when claim and completion share one transaction; incomplete claim commit prevention remains a use-case protocol. Outbox/inbox fields prepare later coordination but do not implement retries/fencing/delivery by themselves.

1. **Vì sao outbox và business change dùng chung DbContext/transaction?** Hiểu commit/rollback nguyên tử và vì sao một store tự commit riêng có thể tạo trạng thái lệch.
2. **Unique key khác lease thế nào?** Hiểu unique user/operation/key hoặc consumer/message bảo vệ danh tính bền vững; lease chỉ phân công tạm thời và cần conditional SQL/token kiểm tra khi triển khai worker.
3. **Vì sao không migrate/seed ở startup mỗi replica?** Hiểu schema release là bước operator riêng, role seed phải tường minh, và named volume giữ dữ liệu qua container recreation.

Next: milestone 04 browser Identity authentication with mandatory CSRF; React setup is now 05. Milestones 04–29 and remaining runtime acceptance criteria stay pending.

Suggested commit title: `feat: add PostgreSQL persistence and reliability foundations` (no explicit title was supplied). No commit was created.

## Milestone 04 — Identity browser authentication

### Inspection and scope

Read AGENTS.md, project contract, architecture, status and recorded ADRs; inspected the clean Git checkout, existing API/worker/context/test code, pinned SDK/packages and initial migration before editing. Reused Identity storage and HTTP ProblemDetails/trace/log foundations. SDK 10.0.114, ASP.NET Core/Identity 10.0.14 and the package graph are unchanged. No new dependency, migration, authentication token infrastructure or business CRUD was added. No agents, commit, push, deployment or data reset was performed.

Scope adjustment reported: the current request places shared Data Protection key implementation in **26**. The earlier roadmap had local deployment in 25 and fault tests in 26. ADR-010 explicitly extends 26 with replica key/TLS/proxy hardening, preserving its fault-suite prerequisites. Current key configuration only sets application name OrderFlow and uses the framework local ring; it does not claim multi-replica readiness.

### Completed acceptance

| Requirement | Result / evidence |
| --- | --- |
| Register/login/logout/me/token endpoints | Complete: actual HTTP + PostgreSQL tests, no auto-login on registration; logout clears this browser session |
| Customer-only registration/no privileged input | Complete: only email/password; unknown fields rejected; six privileged-field cases, normalized duplicates and concurrent duplicate constraints tested; creation/membership atomic |
| Standard Identity password/normalization/lockout | Complete: standard hashing/validators; email as username, unique normalized username index; 5 failures/15 minutes, PostgreSQL row lock prevents lost increments across API hosts |
| Cookies/HTTP status/roles | Complete: host-only Secure HttpOnly SameSite settings, 8-hour non-sliding session, API 401/403 without redirects, exact AdminOnly/CustomerOnly policies; Admin not implicitly Customer |
| Explicit antiforgery | Complete: global unsafe MVC validation calls IAntiforgery; anonymous login/register and authenticated logout require paired cookie/token; invalid/unpaired/stale/missing tokens and refresh after identity changes tested |
| Development account bootstrap | Complete: disabled by default, explicit Worker command, private required config, non-Development/disabled reject before persistence; no reset/promotion, rerun and weak-password rollback tested |
| Authentication rate limits | Complete: per-IP/per-process fixed windows (20 writes/60 reads per minute), no queue, 429 + Retry-After; test proves separate hosts have separate quotas |
| Local transport safeguards | Complete: HTTPS default; explicit Development HTTP exception has distinct cookie names; non-Development opt-in fails startup |
| Error/log/security boundaries | Complete: existing stable ProblemDetails + trace; auth-specific codes documented, no payload/cookie/token logging; only classified duplicate username maps to validation, unknown infrastructure errors remain sanitized 500 |
| A02 overall | Partial: auth portion complete; actual order ownership/cross-user SQL, full React/browser flows, signed callback isolation and shared-key deployment belong to later milestones |

### Main flow / giải thích bằng tiếng Việt

Client gọi GET antiforgery để nhận request token và cookie HttpOnly; POST phải gửi cả cookie lẫn X-CSRF-TOKEN. Filter toàn cục gọi cơ chế antiforgery chuẩn của ASP.NET Core trước action. Đăng ký chỉ nhận email/password, Identity kiểm tra và băm mật khẩu, transaction lưu user cùng vai trò Customer; server trả 201 nhưng chưa đăng nhập.

Đăng nhập chuẩn hóa email, khóa dòng user trong PostgreSQL, gọi Identity kiểm tra mật khẩu và cập nhật bộ đếm lockout rồi commit. Cookie được phát sau transaction. Client lấy token antiforgery mới vì danh tính đã thay đổi, gọi /me để lấy vai trò có thẩm quyền từ server, rồi POST logout với token hiện tại để xóa cookie. Sai mật khẩu/tài khoản bị khóa trả cùng 401; thiếu vai trò trả 403. Admin seed chỉ chạy bằng lệnh Worker được bật rõ trong Development, không dùng mật khẩu mặc định và không nâng quyền tài khoản sẵn có.

### Important files / migrations

- API [BrowserAuth](../src/OrderFlow.Api/Auth/BrowserAuth.cs), [controller](../src/OrderFlow.Api/Auth/AuthController.cs), [session coordinator](../src/OrderFlow.Api/Auth/AuthSessionService.cs), [antiforgery filter](../src/OrderFlow.Api/Auth/BrowserAntiforgeryFilter.cs), token controller/transport filter/contracts, Program/HTTP middleware registration.
- Application [IdentityPolicy](../src/OrderFlow.Application/Modules/Identity/IdentityPolicy.cs), [development seed](../src/OrderFlow.Application/Modules/Identity/DevelopmentAccountSeeder.cs), seed options and existing persistence registration. Worker explicit account command; both safe appsettings keep Enabled=false.
- [HTTP/PostgreSQL auth tests](../tests/OrderFlow.IntegrationTests/Auth/AuthenticationTests.cs), configuration/composition tests, deterministic registration/login gates and test-only role probes; [seed guard unit tests](../tests/OrderFlow.UnitTests/DevelopmentAccountOptionsTests.cs). Existing persistence fixture users now provide unique emails required by Identity policy. Foundation probes alone explicitly opt out of CSRF; they never ship in the API.
- [Authentication runbook](authentication.md), [ADR-010](adr/0010-identity-cookie-authentication.md), README, .env.example, spec/architecture/error/persistence/status documents.
- **New database migrations: none.** Existing 20261102023642_InitialPersistence supports this feature. Integration tests migrate fresh disposable databases and verify no pending migrations/model drift.

### Exact checks and results

Tooling environment: DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache. Build/TRX/verification scripts/logs are under /tmp/orderflow-auth, outside the repository. PostgreSQL test connection is injected privately from the existing mode-0600 verification secret file; no credential value is printed or checked into source.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-auth/artifacts` — exit 0, five projects, existing pins/lockfiles retained; no dependency additions.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-auth/artifacts` — final exit 0, **0 warnings/errors**. Restricted execution initially exited 1 without diagnostics; running with permitted host execution succeeded. Initial antiforgery integration failures exposed MVC's missing view-filter registration under AddControllers; replaced that attribute with a small filter calling supported IAntiforgery directly. Two subsequent configuration tests exposed early configuration capture; final settings now resolve after all host configuration is applied, and startup validates the result.
3. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-auth/artifacts --logger 'trx;LogFilePrefix=full' --results-directory /tmp/orderflow-auth/test-results` with private ORDERFLOW_TEST_POSTGRES — final exit 0, **48 unit + 80 integration/HTTP = 128 passed, 0 failed/skipped**. Final TRX: full_net10.0_20261102161029.trx and full_net10.0_20261102161035.trx. Of these, 30 require actual PostgreSQL (10 existing persistence + 20 new auth/config tests); 98 need no external service. Isolated auth composition test explicitly removes DB configuration and still gets token/401. Concurrency gates prove both participants reached validation/lock operations; no arbitrary sleeps establish races.
4. `/tmp/orderflow-auth/verify-hosts.py` launches compiled API and worker DLLs from their Release output folders independently, without connection configuration. Real Kestrel loopback liveness 200, insecure token endpoint 400, anonymous /me 401, Production OpenAPI/test-only probes 404; each host handles SIGTERM and exits 0. Worker seed-development-account refuses Production even when enabled, and Development when disabled, before resolving persistence. No developer account was created by this launch check.
5. PostgreSQL 18.6 verification Compose profile was restarted healthy using the existing temporary daemon override (the Snap daemon cannot bind this tool session's secret file). After tests, only verification container/network were stopped/removed with `down` **without --volumes**; named volume orderflow-m03-check_postgres_data is retained. No developer database/volume was reset.
6. `git diff --check` and final repository inspection passed: 100 deliverable files, 123 valid local Markdown links, valid JSON/XML, balanced fenced blocks, no build output/private verification credential. No generated output, private cookie jar, real credential or new migration is included. No test run is claimed for React, actual browser HTTPS/proxy behavior, shared Data Protection replicas or production deployment.

### Manual verification, remaining work and trade-offs

Run README locked restore/build/test with private PostgreSQL settings; follow [authentication.md](authentication.md) for migration/roles, trusted local HTTPS or explicit loopback HTTP, account seeding, cookie-jar curl and browser fetch steps. Refresh the CSRF request token after login/logout. Confirm missing token gives 400, anonymous /me 401, privileged registration fields 400, correct login 200, valid logout 204. Production HTTP opt-in and seed command must refuse. No credentials are supplied in tracked examples.

Trade-offs: login row locks serialize one account's attempts; limits are per process/IP, shared by NAT clients and multiplied by replicas. Generic login responses do not promise constant-time account lookup; duplicate registration does not hide account availability. Standard Identity's internal lockout uses its framework UTC wall clock (not an invented custom lockout implementation); application creation/ticket time uses injected TimeProvider. Existing-account seed mismatches fail rather than reset credentials. Logout clears this browser cookie, not immediate revocation of every copied ticket; stamps validate every five minutes. Local Data Protection ring durability/permissions remain operator responsibilities until 26 implements shared protected keys. No email verification/reset, MFA enrollment, role-management UI, distributed rate limiter, auth session store or new frontend is included.

Next: 05 React skeleton/same-origin development proxy; 06 broader authorization conventions/regressions; 13 actual own-order predicates/cross-user 404; 17 signed callback isolation; 26 shared-key deployment hardening; 27 browser acceptance/redaction. Authentication completion does not mark these acceptance criteria complete.

### Interview preparation

1. **Vì sao cookie HttpOnly/SameSite vẫn cần CSRF token?** Hiểu cookie được browser gửi tự động; server phải xác minh cặp token trên thao tác thay đổi dữ liệu, kể cả login, và đổi token sau thay đổi danh tính.
2. **Vì sao dùng Identity và khóa dòng PostgreSQL khi login?** Hiểu hashing/normalization/lockout của framework; hai tiến trình có thể ghi đè failed counter nếu không phối hợp ở DB; không dùng khóa trong process để chứng minh đúng trên nhiều replica.
3. **Vai trò khác quyền sở hữu thế nào?** Hiểu Customer được server gán, Admin không tự là Customer; role policy bảo vệ endpoint, còn query theo owner mới ngăn lộ đơn của người khác. /me hay UI không thay thế server authorization.

Before proceeding, be able to explain the cookie/token refresh flow, registration transaction, lockout serialization, development-only bootstrap and the remaining shared-key/per-process-limit boundaries.

Suggested commit title: `feat: add Identity cookie authentication and antiforgery` (none was supplied; this is the proposed title). No commit was created.

## Milestone 06 — reusable resource authorization

### Inspection and scope

Read AGENTS.md, project spec, architecture, implementation status and relevant ADRs; inspected the existing auth/HTTP/application/test code, SDK/package pins/lockfiles, initial migration and clean Git status (`main...origin/main`) before edits. Existing Identity cookies, CSRF filters, role policy names and ProblemDetails handling were reused. The requested scope matches 06, which depends on completed 04; React 05 remains pending. No behavioral contradiction with the existing ownership contract was found.

No Order entity/endpoint, production representative controller, CRUD, auth bypass, hard-coded identity, package or migration was introduced. Existing role policies now register in one authorization composition method; AdminOnly retains its name and uses the custom privilege handler. No commit/push/deployment/history rewrite or user-data reset occurred. No sub-agent was spawned.

### Completed acceptance

| Requirement | Result / evidence |
| --- | --- |
| Small application-facing caller identity | Complete: ICurrentUser exposes nullable Guid only; scoped HttpCurrentUser reads verified request identity, not headers/query/body, and returns null outside HTTP/for invalid IDs |
| Server-derived ownership contract | Complete: IOwnedResource exposes stored OwnerId; handler requires authenticated Customer and exact matching ID; future resource DTOs must be server-loaded |
| Policies/handlers for Customer/Admin | Complete: named CustomerOwnership policy and CustomerOwnershipHandler, AdminOnly/AdminAccessHandler, existing CustomerOnly endpoint gate; stateless handlers, no DbContext/network dependency |
| Consistent anonymous/wrong-role/missing/non-owner behavior | Complete: 401 unauthenticated, 403 endpoint-role denial, 404 missing/not-owned Customer resource; Admin only on explicit admin path; dual-role users still obey Customer ownership |
| Explicit safe HTTP mapping | Complete: scoped ResourceAccess invokes framework authorization, maps expected denials via ApplicationFailureException; explicit Unauthenticated enum mapping added; unknown infrastructure errors retain sanitized 500 |
| Permission separate from domain rules | Complete: handlers inspect no business state; representative Admin HTTP request still requires CSRF and returns 409 when the independent closed-state guard rejects it |
| Focused representative tests, no production fake endpoints | Complete: 14 policy/identity/access composition cases + 6 real-cookie PostgreSQL HTTP cases + 1 HTTP error-mapping regression; normal production factory returns 404 for test resource URLs |
| A02 overall | Partial: foundation behavior proven; actual Order/list/payment/refund owner-constrained SQL, fulfillment/cancellation state tests, browser flows, callback isolation and shared-key deployment remain feature/deployment work |

### Main flow / giải thích bằng tiếng Việt

Application chỉ nhìn thấy ICurrentUser để lấy ID người dùng hiện tại, không đọc HttpContext hay nhận userId từ client làm căn cứ quyền. Với luồng Customer, endpoint kiểm tra role trước, application query resource theo ID và owner ID, rồi ResourceAccess gọi policy CustomerOwnership trên dữ liệu server đã tải. Handler kiểm tra danh tính hợp lệ, role Customer và OwnerId. Resource thiếu và resource của người khác đều trả cùng 404.

Luồng admin dùng policy AdminOnly; quyền Admin không biến người dùng thành Customer và không bỏ qua ownership trên luồng Customer. Sau khi có quyền, application vẫn phải kiểm tra trạng thái/version/deadline/tồn kho trong unit of work tương ứng. Kiểm thử resource mẫu chứng minh Admin có quyền vẫn nhận 409 nếu quy tắc trạng thái từ chối. Hiện chưa có Order thật để query hay chuyển trạng thái.

### Important files and migrations

- Application [ICurrentUser](../src/OrderFlow.Application/Common/Authorization/ICurrentUser.cs), [IOwnedResource](../src/OrderFlow.Application/Common/Authorization/IOwnedResource.cs), and existing ApplicationError with explicit Unauthenticated value.
- API [authorization registration/policy names](../src/OrderFlow.Api/Auth/Authorization/AuthorizationPolicies.cs), [ownership handler](../src/OrderFlow.Api/Auth/Authorization/CustomerOwnershipHandler.cs), [Admin handler](../src/OrderFlow.Api/Auth/Authorization/AdminAccessHandler.cs), [ResourceAccess](../src/OrderFlow.Api/Auth/Authorization/ResourceAccess.cs), HttpCurrentUser/UserIdentity; BrowserAuth delegates registration to these policies, ApiProblems adds the expected 401 mapping.
- [Policy/identity tests](../tests/OrderFlow.IntegrationTests/Authorization/AuthorizationPolicyTests.cs), [HTTP tests](../tests/OrderFlow.IntegrationTests/Authorization/ResourceAuthorizationHttpTests.cs), test-only RepresentativeResourceStore/controller and existing auth factory/probes/composition/error tests.
- [Authorization guide](authorization.md), [ADR-011](adr/0011-resource-authorization.md), README, spec/architecture/authentication/error/status docs.
- **New database migrations: none.** Existing migration/model are unchanged; full persistence suite still verifies fresh migration/no model drift. SDK and all package/tool/lockfile versions remain unchanged.

### Exact checks and results

Used DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache. All new generated artifacts/TRX/scripts/logs are under /tmp/orderflow-authorization, outside the repository. Verification uses the existing private PostgreSQL secret file through environment variables, never printed/tracked. HTTP test users/passwords/IDs are generated per case.

1. `dotnet --version` — 10.0.114. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-authorization/artifacts` — exit 0, all five projects. No dependency changes or audit bypass.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-authorization/artifacts` — exit 0, **0 warnings/errors**.
3. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-authorization/artifacts --logger 'trx;LogFilePrefix=authorization' --results-directory /tmp/orderflow-authorization/test-results` with private ORDERFLOW_TEST_POSTGRES — exit 0, **48 unit + 101 integration/policy/HTTP = 149 passed, 0 failed/skipped**. TRX files authorization_net10.0_20261103160708.trx and authorization_net10.0_20261103160718.trx. There are 36 actual PostgreSQL cases and 113 external-service-free cases. The six new HTTP cases use real PostgreSQL Identity storage + production login/cookie/CSRF, with no test auth scheme. Only representative resource data is a test dictionary; this is not Order SQL evidence.
4. PostgreSQL 18.6 Compose verification profile started with `up -d --wait --wait-timeout 90`, healthy. Same existing Snap daemon workaround as milestones 03/04: verification-only override under /tmp because the daemon cannot bind this tool session's secret file. After tests, `down` exited 0 and removed only the verification container/network without --volumes; volume inspection confirmed orderflow-m03-check_postgres_data is retained. Tests create/drop only their uniquely named fixture database; developer DB/volume is not reset.
5. Final `git diff --check` and repository checks passed: 113 deliverable files, 148 valid local Markdown links, valid JSON/XML, balanced fences, unchanged roadmap IDs 01–29 and no generated output/private verification credential. No actual browser/proxy/shared-key deployment or real Order ownership SQL/transaction test is claimed. Existing composition tests still cover API liveness and separate worker startup without external services; no additional host feature requires another manual launch.

### Manual verification, trade-offs and remaining work

Follow [authorization.md](authorization.md): after locked restore/build and private PostgreSQL configuration, run `dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthorizationPolicyTests'` for focused no-service policy cases, or filter `FullyQualifiedName~ResourceAuthorizationHttpTests` for cookie/PostgreSQL behavior. Add the matching --artifacts-path if using an external build output. Run the normal API and request /_tests/resource-authorization/customer/{generated-uuid}; it must be 404, since test controllers are not shipped. There is no fake production Order endpoint to exercise manually. Existing auth cookie/CSRF curl/browser instructions remain valid.

Trade-offs: explicit imperative resource authorization requires later callers to load trusted resources and invoke the guard; it is not an automatic database filter. The v1 identity contract accepts one authenticated Identity identity with one nonempty user UUID; ambiguous/malformed claims fail closed, and no HTTP context means no caller ID. Resource ownership requirements remain independent of Admin permission and domain transition rules. Role/cookie security-stamp freshness retains milestone-04 behavior. IAuthorizationService has no CancellationToken API; the adapter checks cancellation around its in-memory evaluation, and later queries must propagate cancellation themselves.

Remaining integration is explicit: 13 must implement owner-constrained Order/payment/refund reads/list/cancel queries and real cross-user HTTP/SQL evidence; 19 must integrate Admin access with actual fulfillment state/version rules; 17/26/27 retain signed callback/shared-key/browser tests. 05 React is still pending. No A02 completion is claimed beyond representative-resource foundations.

### Interview preparation

1. **Vì sao Authorize attribute chưa đủ cho ownership?** Nó chạy trước lúc tải entity; role gate đi trước query, còn ownership handler cần resource server đã tải và query phải lọc owner.
2. **Vì sao non-owner trả 404, sai role trả 403?** Hiểu phân biệt quyền vào endpoint với sự tồn tại của resource; không để Customer dò đơn người khác và không đổi mọi 403 thành 404.
3. **Admin có quyền thì vì sao vẫn nhận 409?** Authorization trả lời có được thao tác hay không; state machine/version/deadline và transaction mới quyết định thao tác hiện tại có hợp lệ hay không.

Before proceeding, explain where the trusted caller/resource IDs originate, how query predicates and handlers work together, why Admin does not bypass Customer ownership, and which actual Order integrations/tests still remain.

Suggested commit title: `feat: add reusable resource authorization policies` (none was supplied; this is the proposed title). No commit was created.

## Milestone 07 — Products and Categories Catalog

### Inspection, scope and decisions

Read AGENTS.md, project spec, architecture/status and relevant ADRs; inspected clean Git status (`main...origin/main`), current Identity/resource authorization/HTTP/money/paging primitives, EF context/migration/model snapshot, tests and all pinned SDK/package/tool versions before editing. Reused those foundations and extracted the existing real-cookie authenticated-browser test helper for reuse. No dependency/SDK version change, generic repository, cache/upload/provider or premature Inventory/Order model was added. No commit/push/deployment/history rewrite or user-data reset occurred; no sub-agent was spawned.

Reported scope clarifications before dependent behavior: Categories are new to the original product-only contract; advisory inventory summaries/zero-stock initialization remain 08/09, so this Catalog API returns no fake inventory field. ADR-012 fixes one category per product, non-unique bounded category labels, category-based public visibility, archive/restore/version semantics and literal substring search. Categories introduce a lock level before Product writes; architecture now records that order for future checkout integration. Existing snapshots/payments are not invalidated by catalog archival; actual Order entities/tests remain later work.

### Completed acceptance

| Requirement | Result / evidence |
| --- | --- |
| Product/Category domain and schema | Complete: server-generated stable UUIDs; required bounded/trimmed names; immutable normalized ASCII SKU; positive numeric(18,2) USD price; required restrictive Category FK; active flags, UTC microsecond CreatedAt and concurrency versions |
| Admin create/update/archive | Complete: explicit typed endpoints/requests, Admin policy + global CSRF, expectedVersion, 201 Location, version/unique/state conflicts; no DELETE or inventory input accepted |
| Public/customer read/search | Complete: active Product AND active Category visibility; public categories active even empty; admin can read archived/hidden rows; no caller-supplied admin flag |
| Archive decisions | Complete: category archive hides children without rewriting their own active/version state; restore preserves individual product archives; SKU stays reserved; current-version archive repeat is a conditional no-op |
| Filtering/pagination/sorting | Complete: literal escaped ILIKE name/SKU search, categoryId filter, 1–100 page size/default 20/offset bounds, empty past-end pages, filtered counts and unique Id tie-breakers for every allowlisted sort |
| Read-query behavior | Complete: no-tracking SQL DTO projection; count/page awaited sequentially with CancellationToken; no unbounded list endpoint or concurrent DbContext use |
| Coordination/index claims | Complete: PostgreSQL unique SKU; EF version guards; category row lock/fresh state for product writes; no claim B-tree accelerates leading-wildcard substring queries; index/schema inspection and two deterministic HTTP races passed |
| DTOs/money/OpenAPI | Complete: price strings/currency USD, ordinary integers remain numeric; positive Catalog schema separate from shared zero-allowed money, required non-null update schema, actual typed paths/responses/errors; no class Produces filter changes ProblemDetails content type |
| A03 overall | Partial beyond Catalog: Order line snapshots, totals/overflow and exact frontend arithmetic remain 11/12/20/21 |

### Main flow / giải thích bằng tiếng Việt

Request admin đi qua cookie authentication, policy Admin và CSRF trước controller. Controller gọi CatalogService; domain chuẩn hóa SKU/kiểm tra tên/giá/version. Product create/update mở transaction, khóa Category trong PostgreSQL, đọc trạng thái mới nhất rồi ghi Product; unique SKU và điều kiện Version trong SQL xử lý tranh chấp giữa các request. Lỗi SKU trùng hoặc version cũ trả 409, lỗi input trả 400, lỗi hạ tầng không xác định vẫn trả 500 an toàn.

Luồng đọc lọc Product.IsActive và Category.IsActive ở SQL, thêm keyword/category filter, đếm và lấy trang theo sort có Id làm tie-breaker, rồi project DTO không tracking. Giá được serialize thành chuỗi USD. Archive Category chỉ đổi Category; sản phẩm vẫn được giữ cho admin và tương lai Order tham chiếu, nhưng biến mất khỏi API công khai. Khôi phục Category không tự khôi phục Product đã archive riêng.

### Important files and migration

- Application Modules/Catalog: [Product](../src/OrderFlow.Application/Modules/Catalog/Product.cs), [Category](../src/OrderFlow.Application/Modules/Catalog/Category.cs), CatalogRules, [CatalogService](../src/OrderFlow.Application/Modules/Catalog/CatalogService.cs), explicit request/response/query DTOs.
- Persistence [CatalogConfiguration](../src/OrderFlow.Application/Persistence/CatalogConfiguration.cs), shared context additions and **[20261103093358_AddCatalog](../src/OrderFlow.Application/Persistence/Migrations/20261103093358_AddCatalog.cs)** with generated designer/model snapshot. Creates catalog.Categories/Products only; Up adds schema/tables/indexes/FK/checks, no existing-data rewrite. Down is destructive to Catalog data and was not run.
- API [public controller](../src/OrderFlow.Api/Catalog/PublicCatalogController.cs), AdminProducts/AdminCategories controllers, CatalogRegistration/OpenAPI; existing ApiProblems adds sku_conflict, Program composes the feature.
- Tests [Catalog HTTP](../tests/OrderFlow.IntegrationTests/Catalog/CatalogHttpTests.cs), read/OpenAPI, constraints/mapping, concurrency gates, [domain tests](../tests/OrderFlow.UnitTests/CatalogDomainTests.cs); reusable AuthenticatedBrowser helper and adapted migration-count/HTTP error mapping checks.
- [Catalog runbook](catalog.md), [ADR-012](adr/0012-catalog-products-categories.md), README/spec/architecture/persistence/error/status/module docs.

### Exact checks and results

Tooling: DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache. All generated verification output is under /tmp/orderflow-catalog. PostgreSQL credentials are read privately from the existing mode-0600 file and passed through environment; no real credentials/cookies/config are printed/tracked. SDK remains 10.0.114, ASP.NET/EF/Identity/tool 10.0.14, Npgsql 10.0.3. Official Npgsql translations, EF concurrency and ASP.NET OpenAPI APIs were consulted.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-catalog/artifacts` — exit 0, all five projects, existing package pins/lockfiles unchanged.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-catalog/artifacts` — final exit 0, **0 warnings/errors**.
3. `dotnet ef migrations add AddCatalog --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` — exit 0, generated/reviewed additive migration. EF commands set ArtifactsPath=/tmp/orderflow-catalog/artifacts to match build/metadata paths.
4. `dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` — exit 0 on existing verification DB, applies AddCatalog and preserves Identity/reliability data; repeated command exit 0/no migration. `dotnet ef migrations has-pending-model-changes` with same flags — exit 0/no changes. `dotnet ef migrations script --idempotent --output /tmp/orderflow-catalog/migration.sql` with same flags — exit 0. The test fixture also migrates a fresh isolated DB through both migrations, checks no pending migration/model drift and drops only its generated DB.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-catalog/artifacts --logger 'trx;LogFilePrefix=catalog' --results-directory /tmp/orderflow-catalog/test-results` with private ORDERFLOW_TEST_POSTGRES — final exit 0: **57 unit + 135 integration/HTTP = 192 passed, 0 failed/skipped**. Final TRX catalog_net10.0_20261103164955.trx and catalog_net10.0_20261103165008.trx. There are 69 PostgreSQL cases (33 new Catalog) and 123 external-service-free cases. SKU and version races each prove two independent request DbContexts reached the contested insert/lock gate; no sleeps. No EF InMemory or custom test-auth scheme.
6. Initial tests exposed class-level Produces application/json overriding validation ProblemDetails content types, raw PostgreSQL RESTRICT's 23001 code (not 23503), and nullable/reference OpenAPI money/currency fields. Fixed those contracts/expectations; final tests verify safe errors, positive string money, non-null required update metadata and fixed USD response. These failures were not reported as passing.
7. `/tmp/orderflow-catalog/read-openapi.py` independently launches compiled API on Kestrel loopback in Development, reads generated OpenAPI and public products/categories pages using private verification DB configuration, then SIGTERM exit 0. No catalog/user data is created by this smoke check. Existing composition tests continue to cover separate worker startup; no worker job was added.
8. PostgreSQL verification Compose starts healthy with the existing /tmp daemon override needed because Snap cannot bind this tool session's secret file. Final read-only counts were users=0, roles=2, products=0, categories=0, migrations=2, confirming no catalog/account seed was introduced into the verification DB. Cleanup down exited 0 without --volumes, and volume inspection confirmed orderflow-m03-check_postgres_data is retained. Final git diff --check and repository checks passed: 133 deliverable files, 176 valid local Markdown links, valid JSON/XML/balanced fences, preserved roadmap IDs and unchanged package/SDK/lockfile graph; no generated output or private verification credential in deliverables. No developer data reset or production deployment was performed.

### Manual steps, trade-offs and remaining work

Use README locked restore/build, then the dedicated migration command in [persistence](persistence.md). Follow [Catalog manual walkthrough](catalog.md): explicit Development Admin setup, cookie jar + fresh CSRF token; create Category then Product; search/filter/page publicly; edit using returned version; repeat stale version for 409; archive/restore and compare public 404/admin 200; duplicate archived SKU still 409; non-admin/missing CSRF/inventory fields must fail. Sample data is created only when you explicitly run that walkthrough; no automatic seed/reset exists.

Trade-offs: leading-wildcard ILIKE may scan/sort and has no arbitrary-substring B-tree performance claim; no large-data benchmark/pg_trgm index is implemented. Count/page and offset navigation can drift under concurrent writes; unique tie-breakers make unchanged data deterministic. Category archive affects public visibility without changing child versions, requiring future client invalidation. Category row locks constrain throughput for one category. Raw SQL numeric typemod may coerce scale; API/domain reject it before SQL. Existing Admin cookie freshness/shared-key limits remain as documented in 04/26.

Remaining: Inventory balances/default-zero initialization/advisory counts in 08/09, actual Order snapshots/checkout eligibility/locks in 11/12, frontend catalog/cents state in 20, image/upload/cache only when requested, production runtime DB privileges/shared keys/performance/browser acceptance later. Catalog adds no fake stock and proves no nonexistent Order FK/snapshot behavior. A03/A01/A02 completion remains limited to actual evidence.

### Interview preparation

1. **Vì sao unique SKU và expectedVersion giải quyết hai vấn đề khác nhau?** Unique index chặn hai lần tạo cùng danh tính SKU; version predicate chặn ghi đè bản sửa đã thay đổi, kể cả nhiều API process.
2. **ILIKE '%text%' có dùng B-tree thông thường để tăng tốc không?** Hiểu literal escaping, leading wildcard, scan/sort và lý do phải đo query plan trước khi thêm pg_trgm/GIN.
3. **Archive Category ảnh hưởng Product và Order thế nào?** Hiểu visibility bằng hai active flags, không xóa/đổi child state; Order tương lai phải giữ FK/snapshot và checkout phải revalidate dưới locks, không tin catalog UI.

Before proceeding, explain the controller → domain/transaction → DTO flow, category/product lock order, filtered paging/tie-breakers, archival visibility and which inventory/order guarantees are still absent.

Suggested commit title: `feat: add product and category catalog` (none was supplied; this is the proposed title). No commit was created.

## Milestone 08 — Inventory balances and append-only StockMovements

### Inspection, scope and decisions

Read AGENTS.md, contract, architecture/status and ADR-003/004/012; inspected clean Git (`main...origin/main`), existing Catalog create/read flows, Identity/resource policies/ICurrentUser, scoped EF context, migrations/mappings, package/SDK/tool pins and PostgreSQL tests before editing. Reused the three-project/module architecture, real cookie/CSRF tests and existing error/pagination/time contracts. No package/version change, generic repository, cache/upload, Order entity or worker job was introduced. No commit, push, deployment, history rewrite, user-data reset or agent delegation occurred.

Reported the scope adjustment before implementation: the request brings Admin stock receipts/adjustments/replay/race evidence into 08 (formerly 09), and assigns full order reservation behavior to 09 (formerly some schema in 11). [ADR-013](adr/0013-inventory-ledger-and-stock-commands.md) updates the roadmap: 09 includes its required Order identity/FK prerequisites; 11 completes lines/lifecycle/events, and checkout/worker/payment integration remains later. No premature reservation enum/interface or fake reservation endpoint was added.

### Completed acceptance

| Requirement | Result / evidence |
| --- | --- |
| Inventory for every Product | Complete: Product insert trigger creates zero balance/opening movement in the same transaction; migration backfills existing active/archived products once, without nonzero stock or fake human account |
| Durable stock invariant | Complete: int OnHand/Reserved, bigint Revision; DB checks 0 <= Reserved <= OnHand and revision >= 0; derived Available only; direct SQL invalid states rejected |
| Admin receipt/adjustment use cases | Complete: positive receipt/nonzero signed delta, bounded required reason, actor from authenticated ICurrentUser and actual Identity user; Admin + CSRF, no actor/stock/derived input bypass |
| Atomic history and balance | Complete: one short PostgreSQL transaction, row locks, tracked EF update + inserted snapshot movement; rollback and injected write-failure tests prove no partial stock/history |
| Append-only audit | Complete: immutable kind/actor/reason/deltas/before-after facts/reference/revision/UTC time; DB UPDATE/DELETE/TRUNCATE protection, EF saved-property protection and restrictive audit FKs |
| Process-safe concurrency | Complete: two API hosts contend on one stock row safely, concurrent receipts retain increments, duplicate commands cannot apply twice, cross-product collision rolls back loser |
| Multi-row ordering | Complete: bounded/deduplicated canonical UUID ordering, sequential FOR UPDATE locks; opposite-order inputs in separate units prove the same lock order and correct committed stock |
| Stale tracked state | Complete: no ExecuteUpdate; fresh locking query explicitly refreshes current/original snapshots and resets unchanged tracked state; pending unsaved edits rejected, regression test passed |
| Durable operation identity | Complete: unique namespace/operation UUID; compare normalized actor/product/kind/delta/reason; same command returns original movement, changed command 409, failed transaction consumes no key |
| Read DTOs/history | Complete: Product Available from actual stock; Admin OnHand/Reserved/Revision and paged history (max100, unique revision order), numeric counts, string enum names and property-specific UTC Z/microsecond timestamp |
| Deferred criteria | Full reservation lifecycle/history deltas are 09; Order checkout/payment/worker/outbox integration, operational privileges/shared keys and frontend remain later. A04 stock commands proven; A07/A16–A19 overall remain partial beyond current evidence |

### Main flow / giải thích bằng tiếng Việt

Tạo Product ghi bảng Products; trigger PostgreSQL tạo balance OnHand=Reserved=0 và movement khởi tạo System ngay trong transaction đó. Migration áp dụng cùng quy tắc cho Product cũ, không tự tạo lượng hàng thực tế. Public Product trả Available = OnHand - Reserved từ dữ liệu thật, client không được ghi các giá trị này.

Admin gửi receipt/adjustment với operationId, quantity/delta và lý do. API kiểm tra cookie/role/CSRF; InventoryService lấy actor từ ICurrentUser, kiểm tra replay, mở transaction và khóa balance theo thứ tự ProductId. Balance được đọc mới dưới khóa rồi domain kiểm tra không giảm dưới Reserved/không overflow. Service ghi cả balance và movement rồi commit. Movement không thể sửa/xóa bằng DML thông thường; muốn sửa tồn kho phải thêm adjustment có lý do. Gửi lại cùng operation trả movement gốc, không cộng/trừ thêm.

### Important files and migration

- Application [InventoryBalance](../src/OrderFlow.Application/Modules/Inventory/InventoryBalance.cs), [StockMovement](../src/OrderFlow.Application/Modules/Inventory/StockMovement.cs), [InventoryService](../src/OrderFlow.Application/Modules/Inventory/InventoryService.cs), [InventoryLocking](../src/OrderFlow.Application/Modules/Inventory/InventoryLocking.cs) and explicit request/response/time JSON contracts.
- Persistence [InventoryConfiguration](../src/OrderFlow.Application/Persistence/InventoryConfiguration.cs), shared context, **[20261104083112_AddInventory](../src/OrderFlow.Application/Persistence/Migrations/20261104083112_AddInventory.cs)** with generated designer/snapshot and reviewed trigger/backfill SQL. Up is additive; Down would delete inventory/history and was not run.
- API [AdminInventoryController](../src/OrderFlow.Api/Inventory/AdminInventoryController.cs), InventoryRegistration, Program and explicit stock-limit error; Catalog DTO/projection/mutation response reads actual Available and keeps stock writes in Inventory.
- [HTTP tests](../tests/OrderFlow.IntegrationTests/Inventory/InventoryHttpTests.cs), [concurrency tests](../tests/OrderFlow.IntegrationTests/Inventory/InventoryConcurrencyTests.cs), [SQL/fault/backfill/tracking tests](../tests/OrderFlow.IntegrationTests/Inventory/InventoryPersistenceTests.cs), [domain tests](../tests/OrderFlow.UnitTests/InventoryDomainTests.cs), migration count/error mapping/OpenAPI regressions.
- [Inventory runbook](inventory.md), [ADR-013](adr/0013-inventory-ledger-and-stock-commands.md), README/spec/architecture/persistence/Catalog/status/module docs.

### Exact checks and results

Tool environment: DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache. Generated artifacts/TRX/scripts/SQL/logs are under /tmp/orderflow-inventory. Credentials come privately from the existing mode-0600 verification file through environment variables, never printed/tracked. SDK remains 10.0.114, ASP.NET/EF/Identity/tool 10.0.14 and Npgsql 10.0.3. Official PostgreSQL locking/trigger/UUID and EF tracking APIs were consulted.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-inventory/artifacts` — exit 0/all five projects, unchanged package/SDK/lockfiles.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-inventory/artifacts` — final exit 0, **0 warnings/errors**. An intermediate build flagged EF1002 on test-only interpolated DDL; those calls now use explicit Npgsql commands with generated UUID identifiers, without analyzer suppression or unsafe production interpolation.
3. `dotnet ef migrations add AddInventory --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` — exit 0. Reviewed/generated schema, then added atomic Product initialization/backfill and UPDATE/DELETE/TRUNCATE guards. EF commands set ArtifactsPath=/tmp/orderflow-inventory/artifacts to match the build.
4. `dotnet ef database update --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` — exit 0, additive Inventory migration on retained verification DB; repeat exit 0/no changes. `dotnet ef migrations has-pending-model-changes` with the same flags — exit 0/no drift. `dotnet ef migrations script --idempotent --output /tmp/orderflow-inventory/migration.sql` with the same flags — exit 0. Tests independently migrate a fresh DB and upgrade an owned DB containing active/archived legacy products; each gets exactly one zero opening fact.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-inventory/artifacts --logger 'trx;LogFilePrefix=inventory' --results-directory /tmp/orderflow-inventory/test-results` with private ORDERFLOW_TEST_POSTGRES — final exit 0: **65 unit + 163 integration/HTTP = 228 passed, 0 failed/skipped**. Final TRX inventory_net10.0_20261104155955.trx and inventory_net10.0_20261104160017.trx. There are 96 PostgreSQL cases and 132 external-service-free cases. Stock tests use real PostgreSQL/Identity cookies/CSRF, no EF InMemory or test auth scheme. Race barriers count distinct participating DbContexts; no sleeps. Test DB cleanup drops only generated databases owned by the fixtures.
6. Real OpenAPI inspection found inherited error response attributes suppressed default 200 schemas on Inventory and existing Catalog reads/updates, and the custom timestamp needed an explicit string type. Added action-specific success metadata and timestamp schema plus regressions, preserving ProblemDetails content types and business behavior. `/tmp/orderflow-inventory/read-openapi.py` independently starts Kestrel loopback in Development, verifies bounded public products/categories 200, generated typed OpenAPI, and SIGTERM exit 0. No catalog/account data is created by this smoke check.
7. PostgreSQL Compose verification starts healthy with the existing temporary Snap daemon workaround; the committed Compose/secret setup is unchanged. Final read-only verification counts were users=0, roles=2, products=0, balances=0, movements=0, migrations=3: only additive schema changes, no sample stock/account seed. Cleanup down exited 0 without --volumes, and volume inspection confirmed orderflow-m03-check_postgres_data is retained. Final git diff --check and repository checks passed: 149 deliverable files, 211 valid local Markdown links, valid JSON/XML/balanced fences, retained roadmap IDs and unchanged package/SDK/lockfile graph; no generated output or private verification credential in deliverables.

### Manual verification and trade-offs

Apply migrations explicitly after README restore/build. Follow [Inventory runbook](inventory.md): Development Admin/cookie jar + fresh CSRF; create Product and observe zero/Initialization; receive 10 with a retained operation UUID; replay it and observe stock still 10; new adjustment -3 leaves 7; excessive removal gives 409 with no new movement. Read history/current inventory separately; a replay is an old snapshot. Missing CSRF, wrong role, spoofed actor/Available/Reserved or invalid quantities must fail. Never set Reserved via a production shortcut; only tests model an existing reservation until 09.

Trade-offs: row locks serialize one product; multi-row callers must submit their complete ID set in a single ordered locking call. Available is advisory and independent of Product.Version. Revision order is deterministic, while count/page can drift under concurrent inserts. Initialization triggers add reviewed database behavior but cover every insertion path; their timestamp is logical Product.CreatedAt, not a physical receipt. System opening facts have no fake human actor. Audit rows/operation identities are retained; corrections are new movements. Runtime least privilege/shared Data Protection/browser acceptance and large-data benchmarks are not implemented here. Privileged schema owners can alter/drop protections; normal DML cannot edit the ledger.

Remaining: full reservation model and required Order identity/FK prerequisites in 09, line snapshots/lifecycle/events in 11, authoritative checkout/idempotency in 10/12, expiry worker/payment transitions later. Reservation-related history/checks must be extended in 09 without mutating existing facts. No claim of complete reservation/Order reconciliation or external exactly-once delivery is made.

### Interview preparation

1. **Vì sao transaction chưa đủ nếu cùng đọc tồn kho cũ?** Hiểu FOR UPDATE và thứ tự khóa ProductId bảo vệ read/check/write giữa nhiều process; constraints là lớp bảo vệ cuối.
2. **Vì sao movement phải ghi cùng balance và không được sửa?** Hiểu rollback, before/after/delta/actor/reason, correction bằng movement mới và operation UUID tránh áp dụng lặp khi mất response.
3. **Vì sao phải refresh entity đã track trước đó?** EF có thể trả instance cũ; cần snapshot mới dưới khóa, không trộn ExecuteUpdate với dữ liệu tracked rồi ghi đè tồn kho đã đổi.

Before proceeding, explain zero initialization vs physical receipt, server-derived actor/Available, reserved-floor/overflow behavior, ordered locking/tracker reconciliation and the scope of the 09 reservation work.

Suggested commit title: `feat: add inventory balances and append-only stock movements` (none was supplied; this is the proposed title). No commit was created.


## Milestone 08a — reusable integration-test harness (2026-11-05)

The explicit request adds supplemental 08a after 08, without consuming/redefining 09 or implementing reservations/expiry/jobs. No behavioral contradiction with the specification/architecture/ADRs was found. ADR-014 supersedes the old shared-test-database arrangement from the initial persistence harness; it does not change production behavior. Initial Git status was clean (`main...origin/main`); root AGENTS.md, specification, architecture, status, ADRs, existing hosts/services/tests, central pins/locks and all three migrations were inspected. No agents, commits, pushes, deployment, history changes or developer-data reset were used.

### Completed acceptance and flow

| Criterion | Result and evidence |
| --- | --- |
| Reusable WebApplicationFactory + actual PostgreSQL | Complete: existing AuthApiFactory is used by Catalog, Inventory, Identity and representative-resource policy HTTP tests; default fixture starts Testcontainers PostgreSQL 18.6 with the same digest as Compose. Optional private ORDERFLOW_TEST_POSTGRES uses an actual server, with no provider substitution |
| Real migrations and independent data | Complete: every fact/theory row inherits PostgresTest; each gets a generated database and full IMigrator migration chain before the body. Two theory rows write the same fixed SKU independently after empty-table assertions; upgrade/backfill uses another owned database at AddCatalog |
| Lifecycle and bounded cleanup | Complete for normal/initialization-failure paths: 60s create/migrate cancellation, 30s forced database-drop budget, nonpooled connections, independent cleanup token, disposal failure reporting; Testcontainers disposal is caller-bounded at 30s and Ryuk stays enabled. Open-connection/repeated-disposal regression passes; Docker inventory found 0 remaining among 16 containers recorded in these runs |
| Authentication/CSRF without weakened endpoints | Complete: helper creates generated users/membership via real Identity stores and uses the real HTTPS login/cookie handler. Token helper calls the actual antiforgery endpoint; explicit tokens and cancellation pass through requests. Production rate limits/CSRF/roles/password hashing remain. Production probe-path absence regressions pass |
| Replaceable time | Complete: test-only ConfigureTestServices replaces TimeProvider; manual clock advances UTC without sleeps. Actual Identity/Product timestamps, Inventory movement logical time and expired-cookie 401 are checked |
| Background loops controlled | Complete for current composition: final provider is inspected before host.Start; only framework web-host/Data Protection warmup services are allowed. An added uncontrolled BackgroundService is rejected before startup, including a late WithWebHostBuilder registration. There are no business jobs today |
| Safe deterministic concurrency | Complete for current races: shared RaceRendezvous counts distinct units of work once, waits before common locks/disjoint writes, releases waiting peers on cancellation/timeout and never re-enters at later sorted-row locks. Identity/Catalog/Inventory gates use it; no sleep or production hook/endpoint added |
| Genuine inventory race | Complete: two API hosts, real Admin sessions, independent request contexts, both observed before balance FOR UPDATE; one 201/one 409 insufficient_stock. Fresh DbContext verifies OnHand=3, Reserved=0, Revision=2, revisions 0/1/2, delta reconciliation, winning movement snapshots and no losing operation effect |
| Repeatability and explicit prerequisites/parallelism | Complete: 238-test full suite passed, then all 101 PostgreSQL cases passed in a fresh run/container; 137 service-free tests passed separately. Collection execution is explicitly serial; races use independent contexts. Direct fixture initialization with Docker socket denied fails with the missing-prerequisite message and underlying Permission denied |
| Unit tests remain service-free | Complete: UnitTests project/dependencies unchanged; 65 unit tests pass independently of PostgreSQL |

Luồng chính: xUnit tạo một PostgresServer cho collection. Khi không cấu hình server ngoài, Testcontainers tạo container PostgreSQL dùng cổng ngẫu nhiên và mật khẩu sinh tại runtime. Mỗi test case tạo database riêng, áp dụng migration thật rồi mới tạo WebApplicationFactory. Helper lưu user bằng Identity, đăng nhập qua HTTP và lấy cặp cookie/token CSRF thật. Test có thể truyền ManualTimeProvider; khi kết thúc, client/host/context được dispose trước khi fixture xóa đúng database do nó tạo. Container được dọn khi collection kết thúc.

Race inventory dừng hai request ngay trước câu FOR UPDATE đầu tiên. Chỉ khi hai DbContext khác nhau đã tới điểm này, barrier mới mở. Không request nào phải chờ đối thủ trong khi đang giữ khóa mà đối thủ cần để tới barrier. PostgreSQL quyết định request thắng; test kiểm tra cả HTTP lẫn balance/ledger bằng context mới, không chỉ xem mã phản hồi. Với nhiều hàng, cùng context không chờ barrier lần nữa sau khi đã khóa hàng đầu.

### Important files and migrations

- `tests/OrderFlow.IntegrationTests/Persistence/PostgresServer.cs`: collection container/explicit-server lifetime, PostgresTest per-case setup/teardown.
- `tests/OrderFlow.IntegrationTests/Persistence/PostgresDatabase.cs`: unique owned database, real migration target, SQL/connect/cleanup budgets, initialization-failure cleanup.
- `tests/OrderFlow.IntegrationTests/Auth/AuthApiFactory.cs`, `Auth/AuthenticatedBrowser.cs`, `Auth/BrowserRequests.cs`: real browser authentication/antiforgery, test-only DI clock/interceptors, final-provider hosted-service guard.
- `tests/OrderFlow.IntegrationTests/ManualTimeProvider.cs`, `CommandRaceGate.cs`, `HarnessSynchronizationTests.cs`, `Persistence/HarnessTests.cs`, `Inventory/InventoryConcurrencyTests.cs`, `CompositionSmokeTests.cs`: logical time, synchronization regressions, isolation/cleanup/background/probe checks and durable race assertions. Existing DB test classes now inherit PostgresTest.
- `Directory.Packages.props`, integration `.csproj` and generated `packages.lock.json`: only new direct package is Testcontainers.PostgreSql 4.16.0; no production/unit dependency change.
- `docs/testing.md`, `docs/adr/0014-integration-test-harness.md`, ADR index, README, architecture, persistence/catalog/inventory runbooks and this status: run modes, boundaries, budgets and verification.
- **No new migration.** Existing InitialPersistence, AddCatalog and AddInventory are applied unchanged. No production source change.

### Exact checks and results

Tool environment: SDK 10.0.114, DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache, DOTNET_CLI_TELEMETRY_OPTOUT=1. All temporary builds/TRX/logs/probe files are under `/tmp/orderflow-harness`; no credentials were printed or tracked. Packages restored with vulnerability auditing enabled. Official ASP.NET Core 10 integration/ConfigureTestServices and Testcontainers module/configuration/reaper docs plus NuGet release metadata were consulted before using version-sensitive APIs.

1. Initial default `dotnet restore OrderFlow.slnx` could not write `/home/DUONGNH1/.dotnet/10.0.114.toolpath.sentinel` (read-only filesystem). Writable CLI/cache paths resolved this. Sandbox build returned exit 1 without compiler diagnostics; build/test execution outside the sandbox succeeded. Docker CLI through snap initially reported missing capability; a read-only direct daemon check outside the sandbox returned OK. Docker was available for actual Testcontainers tests.
2. `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-harness/artifacts` — exit 0; generated the integration lockfile for Testcontainers 4.16.0. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-harness/artifacts` — exit 0/all five projects, no audit bypass.
3. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-harness/artifacts` — final exit 0, 0 warnings/errors. Intermediate full/clock checks identified an overly strict hosted-service guard rejecting DataProtectionHostedService; a subsequent guard regression caught late registrations bypassing early descriptor inspection. The final guard checks the built provider before starting it; focused harness + inventory races then passed **10/10**, no skip (focused_net10.0_20261105091157.trx).
4. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-harness/artifacts --logger 'trx;LogFilePrefix=final' --results-directory /tmp/orderflow-harness/results` — exit 0: **65 unit + 173 integration/HTTP = 238 passed, 0 failed/skipped**. Of these, 101 use actual PostgreSQL. TRX: final_net10.0_20261105091236.trx and final_net10.0_20261105091318.trx. Testcontainers supplied the actual DB; no ORDERFLOW_TEST_POSTGRES/Compose fixture was needed.
5. `dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-harness/artifacts --filter 'Category=PostgreSQL' --logger 'trx;LogFilePrefix=repeat-postgres' --results-directory /tmp/orderflow-harness/results` — exit 0: **101 passed, 0 failed/skipped**, fresh container and per-case databases (repeat-postgres_net10.0_20261105091542.trx). This repeats the entire DB collection, not only a single happy path.
6. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-harness/artifacts --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=service-free' --results-directory /tmp/orderflow-harness/results` — exit 0: **65 + 72 = 137 passed, 0 failed/skipped**, no PostgreSQL fixture/container (service-free_net10.0_20261105091536.trx and service-free_net10.0_20261105091538.trx).
7. Attempted missing-Docker simulation with a nonexistent DOCKER_HOST, with/without DOCKER_CONTEXT, still found the reachable local socket and passed the two selected isolation cases. This was **not** claimed as missing-Docker evidence. Instead a temporary console probe called the exact PostgresServer.InitializeAsync under actual restricted Docker socket permissions: `env -u ORDERFLOW_TEST_POSTGRES DOTNET_CLI_HOME=/tmp/orderflow-harness-dotnet dotnet /tmp/orderflow-harness/artifacts/bin/probe/release/probe.dll` — expected nonzero exit **23**, message `PostgreSQL integration prerequisite unavailable`, underlying diagnostic **Permission denied**; no fallback/success skip. The temporary probe is not a repository deliverable. The full VSTest missing-Docker runner path on a machine without any Docker endpoint was not separately exercised.
8. Read-only Docker inventory compared all 16 container IDs recorded as created in these run TRX files — **0 remained** after test process exit; no unrelated container/volume was stopped or removed. `git diff --check` — exit 0. `python3 /tmp/orderflow-harness/check-docs.py` — exit 0: 26 Markdown files, 226 local file links, balanced fences and consistent tables. Anchor targets/remote link availability are outside this structural check.

### Manual verification, trade-offs and remaining work

Run the commands in [testing.md](testing.md): locked restore, Release build, full suite with Docker running, then `--filter 'Category=PostgreSQL'` again to verify repeatability. `--filter 'Category!=PostgreSQL'` runs the external-service-free subset; UnitTests can run alone. An optional private ORDERFLOW_TEST_POSTGRES connection needs CREATEDB and PostgreSQL 18; never target production or print the credential. Manually inspect the inventory race test and its fresh-context ledger assertions. No dedicated application migration command is necessary for tests, because every case applies the real migration chain itself; normal API/worker startup still never migrates.

Trade-offs: per-case migration is slower but avoids resets that bypass append-only triggers and prevents state leakage; serial DB collection execution bounds load. Testcontainers/Ryuk need Docker permissions/image availability. A 30s WaitAsync bounds the disposal caller but cannot cancel the underlying Testcontainers disposal API; Ryuk remains crash recovery. SQL match predicates/framework service type names need review on upgrades. ManualTimeProvider controls UTC decisions, not timers; framework Identity lockout retains its own clock. Resource-policy probes use representative test resources; actual Order SQL ownership is unimplemented. Two-host races are independent DI hosts/contexts in one test process, not OS-process crash tests.

Remaining: default container mode is fully exercised; the retained external-server override was adapted but was not separately reverified in this milestone. Container startup/cleanup timeout fault injection and full runner execution on a Docker-free host remain unverified. Future 09/12–18 tests must reuse safe first-common-lock boundaries, explicitly drive background iterations and add real reservation/expiry/payment invariants. Existing later milestones retain shared protected keys, least-privilege runtime roles, worker leases/crash recovery and frontend/browser acceptance. No claim is made for future criteria merely because the harness supports them.

### Interview preparation and suggested commit

1. **Vì sao dùng database riêng và migration thật thay vì transaction rollback hoặc InMemory?** Hiểu rằng HTTP dùng nhiều connection/transaction, PostgreSQL có row lock/unique index/trigger riêng, và rollback ở test không bao trùm được mọi request; database riêng giữ test độc lập mà không vô hiệu hóa invariant.
2. **Barrier ở đâu sẽ gây deadlock giả?** Hiểu trường hợp request A giữ khóa trước barrier còn B phải lấy chính khóa đó để tới barrier. Đặt hook trước khóa chung đầu tiên, hoặc chỉ đồng bộ các write có khóa trước đó độc lập; luôn đếm context và kiểm tra dữ liệu bền vững sau race.
3. **Thay TimeProvider có tự chạy expiry worker không?** Không. Hiểu sự khác nhau giữa thời gian quyết định nghiệp vụ, timer/scheduling và deadline hạ tầng; advance logical time rồi gọi một iteration có kiểm soát, giữ SQL/cleanup timeout theo thời gian thật.

No commit title was supplied with this request. Suggested: `test: add reusable PostgreSQL integration harness`.

## Milestone 12 — idempotent checkout and required creation prerequisites (2026-11-05)

The latest request delivers checkout now. Initial Git status was clean (`main...origin/main`). Inspected AGENTS.md, specification, architecture/status/ADRs, hosts/auth/HTTP configuration, DbContext/mappings, all three existing migrations, stock locking/audit/idempotency/outbox stores, harness/tests and pinned SDK/packages before editing. No new package, agent, commit/push/deployment/history rewrite or developer-data reset was used.

Reported the contract mismatch before dependent implementation: the original spec/ADR-004 merged duplicate products, while this request rejects them. ADR-015 supersedes only that rule, updates the active spec and records the scoped delivery order. Milestone 12 pulls Active-reservation creation/Order identity from 09, CreateOrder normalization/replay from 10, immutable OrderItems/creation history/event from 11 and owner detail read from 13. Terminal reservation/lifecycle commands are not implicitly completed.

### Acceptance and behavior

| Criterion | Result and evidence |
| --- | --- |
| Authenticated checkout without trusting client authority | Complete: CustomerOnly + production cookie/CSRF, only items/productId/quantity accepted, root/item tampering rejected, owner derives from ICurrentUser |
| Bounded distinct items | Complete: 1–100 nonempty distinct UUIDs, integer quantity 1–1000; duplicates including case variants rejected; HTTP rejects fractional/string/null/missing/out-of-range inputs; quantity 1000 succeeds |
| Authoritative exact snapshots | Complete: locked database SKU/name/unit price, exact positive numeric(18,2) line/aggregate totals, USD decimal strings, overflow rejection, immutable items/history plus protected Order/reservation facts through EF/SQL |
| Fixed deadline | Complete: injected normalized UTC sampled after locks, CreatedAt+15 minutes once; one-way pause/advanced clock verifies late sampling; replay preserves original times |
| Atomic stock and audit | Complete: sorted category/product SHARE locks then sorted balance FOR UPDATE; OnHand unchanged, Reserved/revision incremented, Active reservations and linked Customer reserve movements; PostgreSQL keeps Reserved<=OnHand |
| Entire checkout transaction | Complete: claim + Order/OrderItems/OrderHistory + reservations/balances/stock ledger + Pending OrderCreated outbox + exact response commit together; later-item insufficiency and final outbox faults roll back already executed SQL |
| Idempotency identity and recovery | Complete: versioned canonical JSON + SHA-256, compare both, existing unique user/operation/key index; unique conflict rollback/disposal/detach precedes querying winner; one Order per claim FK; same-body concurrency produces one order, conflicting concurrency returns 409 |
| Exact successful replay/current reads | Complete: original status/body bytes/content type/Location even after catalog edits, elapsed deadline and simulated progressed status; GET owner-constrained SQL returns current status and immutable snapshots; no deadline extension/new effects |
| Genuine last-unit concurrency | Complete: two hosts/users/contexts reach inventory FOR UPDATE before either executes it; prior claims distinct and catalog SHARE locks compatible; one 201/one insufficient_stock 409; fresh context reconciles final inventory, Order/items/reservation/ledger/history/outbox/claims |
| Catalog association revalidation | Complete: single-request one-way pause permits a writer to reassign category before checkout locks; checkout rejects the newly unlocked category instead of acquiring locks out of order, rolls back claim, and same-key retry snapshots the new price/name |
| Failure handling and cancellation | Complete for tested paths: transaction-local 5s lock/20s statement budgets; explicit PostgreSQL 55P03/40P01/40001/57014 (including Npgsql wrapper) map to structured sanitized 503 with same-key retry; unknown errors stay logged/sanitized 500; no implicit retry or external network call in business transaction |
| Existing behavior and real database evidence | Complete: final 80 unit + 211 integration/HTTP = 291 passed; 138 actual PostgreSQL cases, 153 service-free cases. Four real migrations apply per test; existing Catalog/Inventory/Identity/resource-policy/harness tests remain green |

Luồng chính: controller kiểm tra đúng một Idempotency-Key rồi gọi CheckoutService. Service xác định user từ cookie đã xác thực, chuẩn hóa các item khác nhau theo UUID và tính SHA-256. Nếu đã có kết quả thành công thì trả lại bytes cũ. Nếu chưa có, service mở transaction, INSERT claim dưới unique index trước khi lấy khóa nghiệp vụ. Khi trùng claim, transaction thua được rollback/dispose và detach trước khi đọc winner; không tiếp tục truy vấn trong transaction lỗi.

Checkout khóa category/product bằng SHARE theo thứ tự ổn định để bảo vệ giá/trạng thái nhưng vẫn cho các checkout khác đi tới tồn kho. Sau đó khóa balance FOR UPDATE theo product ID, lấy thời gian UTC, tạo đơn PendingPayment cùng snapshot và hạn 15 phút. Mỗi item cập nhật Reserved, tạo reservation và movement, rồi SaveChanges trong cùng transaction. Cuối cùng lưu OrderCreated vào outbox và response 201 nguyên gốc, rồi commit mới trả HTTP. Nếu item sau hoặc outbox lỗi, toàn bộ SQL trước đó bị rollback. GET đọc trạng thái hiện tại bằng cả order ID và owner ID; replay luôn trả snapshot 201 ban đầu.

### Important files and migration

- `src/OrderFlow.Api/Ordering/OrdersController.cs`, `Program.cs`: thin Customer POST/owner GET endpoints, exact stored response delivery, DI; existing global cookie/CSRF/policy behavior retained.
- `src/OrderFlow.Application/Modules/Ordering/CheckoutService.cs`, `CheckoutRequest.cs`, `CheckoutContracts.cs`, `Order.cs`: transaction, canonical/hash/key validation, DTO bytes, exact totals/snapshots/creation history and current detail reads.
- `src/OrderFlow.Application/Modules/Inventory/InventoryReservation.cs`, `InventoryBalance.cs`, `StockMovement.cs`, `InventoryService.cs`, `InventoryContracts.cs`: Active reservation, guarded reserve effect, Customer/order/reservation audit links exposed through Admin history; stock receipt/adjustment behavior retained.
- `src/OrderFlow.Application/Persistence/OrderingConfiguration.cs`, `InventoryConfiguration.cs`, `OrderFlowDbContext.cs`: new tables/sets, checks, unique/restrictive/composite FKs, concurrency/immutability guards.
- **New migration 20261105070955_AddCheckout** (`.cs`, generated `.Designer.cs` and updated model snapshot). Adds Orders/OrderItems/OrderHistory/InventoryReservations, nullable order/reservation ledger links, expanded stock-kind CHECK/FK and reviewed SQL snapshot/history protection triggers. All earlier migrations remain unchanged. No migration was applied to a developer/production database; actual test databases apply/upgrade through all four migrations.
- `tests/OrderFlow.UnitTests/CheckoutDomainTests.cs`: 15 new pure domain/normalization/quantity/key/money/reserve tests, no external-service dependency.
- `tests/OrderFlow.IntegrationTests/Ordering/*`: 37 new PostgreSQL HTTP/constraint/fault/race cases and shared test data/reconciliation. `HttpFoundationTests.cs` adds explicit 503 mapping; persistence migration-count regression now expects four.
- `docs/checkout.md`, ADR-015/ADR index, specification/architecture/status, README and relevant module/persistence/inventory/authorization/HTTP/testing guides: concrete transaction/manual flow, scope corrections and known limits.

### Exact checks and results

Tooling remains SDK 10.0.114, EF/Identity/ASP.NET/tool 10.0.14, Npgsql 10.0.3, Testcontainers.PostgreSql 4.16.0. Used writable DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache and telemetry opt-out. Generated build/TRX/SQL files are under `/tmp/orderflow-checkout`; no real credentials are stored/printed. Docker/Testcontainers actually supplied PostgreSQL, not an emulated provider. Official PostgreSQL locking/unique arbitration/Read Committed and EF transaction/savepoint docs were consulted. The provider source for Npgsql 10.0.3 was checked when a real fault test exposed its InvalidOperationException wrapper.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-checkout/artifacts` — exit 0, all five projects; dependency versions/lockfiles unchanged. `dotnet tool restore` — exit 0, pinned dotnet-ef 10.0.14.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-checkout/artifacts` — final exit 0, **0 warnings/errors**. An intermediate test build reported a missing IdempotencyStatus namespace import; fixed before execution. No analyzer/audit suppression was added.
3. `dotnet ef migrations add AddCheckout --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with matching ArtifactsPath — exit 0, generated the additive migration/model. Reviewed FK/index/check SQL and added snapshot/history protection triggers. Strengthened the not-yet-applied item-money CHECK to reject PostgreSQL numeric NaN and synchronized its generated snapshot before testing. Design-only connection configuration contained no password and did not connect to a database.
4. `dotnet test OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-checkout/artifacts --filter 'FullyQualifiedName~Checkout' --logger 'trx;LogFilePrefix=checkout-focused' --results-directory /tmp/orderflow-checkout/results` — initial focused exit 0, **15 unit + 31 integration cases passed**. Additional storage/category regressions then exposed 55P03 mapping as 500 due to Npgsql's wrapper; handling the known wrapper fixed it. Final focused storage run: **5/5 passed** (checkout-storage_net10.0_20261105142033.trx). The unknown error still returns 500; classified 55P03 returns 503; both roll back earlier writes and permit same-key retry.
5. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with matching ArtifactsPath — exit 0, no model changes. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-checkout/migrations.sql` — exit 0. The fixture applies real Up migrations to fresh owned DBs; existing upgrade/backfill regression upgrades through the new migration as well. No destructive Down was run.
6. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-checkout/artifacts --logger 'trx;LogFilePrefix=checkout-final' --results-directory /tmp/orderflow-checkout/results` — final exit 0: **80 unit + 211 integration/HTTP = 291 passed, 0 failed/skipped**. Final TRX checkout-final_net10.0_20261105143010.trx and checkout-final_net10.0_20261105143118.trx. Includes 138 actual PostgreSQL cases (37 new checkout cases). An earlier full run also passed 291 before refining the last-unit test's gate from catalog arrival to actual inventory arrival.
7. `dotnet test tests/OrderFlow.IntegrationTests -c Release --no-restore --artifacts-path /tmp/orderflow-checkout/artifacts --filter 'FullyQualifiedName~CheckoutConcurrencyTests' --logger 'trx;LogFilePrefix=checkout-races-final' --results-directory /tmp/orderflow-checkout/results` — **6/6 passed**, no skip (checkout-races-final_net10.0_20261105142811.trx), with actual inventory-statement rendezvous; those races passed again in the final full suite.
8. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-checkout/artifacts --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=checkout-service-free' --results-directory /tmp/orderflow-checkout/results` — exit 0: **80 + 73 = 153 passed, 0 failed/skipped**, no PostgreSQL fixture/container; checkout-service-free_net10.0_20261105143010.trx and checkout-service-free_net10.0_20261105143012.trx.
9. Read-only Docker inventory compared 12 container IDs recorded as created in these checkout TRX runs — **0 remained**. No unrelated container/database/volume was stopped or removed. `git diff --check` — exit 0. `python3 /tmp/orderflow-harness/check-docs.py` — exit 0: 28 Markdown files, 250 local file links, balanced fences and consistent tables; remote URL/anchor availability is not part of that structural check.

### Manual verification, trade-offs and remaining work

Follow [checkout.md](checkout.md) for dedicated migrate/no-drift commands and a concrete HTTP walkthrough. Run locked restore, Release build and full suite with Docker running; filter `FullyQualifiedName~Checkout` for the slice or `Category!=PostgreSQL` for service-free tests. Create stock as Admin through existing APIs, log in as a separate Customer, refresh CSRF, submit distinct products with an intention key, GET the returned Location, replay identical bytes and change quantity under the same key for 409. Duplicate/tamper a line for 400, use another Customer to read the order for 404, and request excessive stock under a new key to check rollback. Genuine contention evidence comes from deterministic test barriers, not simultaneous manual curl commands.

Trade-offs: sorted shared catalog locks preserve snapshots and allow actual inventory contention; per-item saves add up to 100 roundtrips but make partial-SQL rollback explicit. Every successful response is retained indefinitely. Clock synchronization is operational. The response/payload format is tied to v1. SQL triggers and EF metadata protect immutable facts; aggregate sums and correct state transitions still require the application transaction. Current enum labels for future statuses do not implement a lifecycle writer.

Remaining/unverified: no release/consume/cancellation/automatic expiry, order listing/Admin fulfillment, payment or outbox dispatch is implemented. Reads can show overdue PendingPayment with stock conservatively reserved, as the existing spec allows during expiry lag. Replay-after-progression uses test-only SQL to simulate a future status writer; it does not prove payment/fulfillment correctness. Actual OS-process crash/connection-loss around commit, real elapsed server lock-timeout/deadlock/serialization fault injection, least-privilege runtime roles, browser UI and external-service delivery remain future scope. The injected 55P03 is real PostgreSQL error propagation but not a real 5-second lock-wait measurement. No exactly-once external-effect claim or automatic business retry was added.

### Interview preparation and suggested commit

1. **Vì sao claim idempotency phải ở cùng transaction và trước khóa tồn kho?** Hiểu unique index phân xử nhiều request, rollback nhả claim, và response chỉ được commit cùng order/reservations/outbox; bộ nhớ trong một process không đủ.
2. **Vì sao phải rollback rồi mới đọc winner sau unique violation?** Hiểu trạng thái transaction PostgreSQL khi câu lệnh lỗi và entity Added còn trong ChangeTracker; không bắt exception rồi tiếp tục query/Save trong unit of work hỏng.
3. **Vì sao khóa giá bằng SHARE rồi khóa tồn bằng UPDATE theo cùng thứ tự?** Hiểu snapshot giá không bị writer đổi giữa checkout, các checkout có thể cùng tới tồn kho, khóa balance ngăn oversell và barrier không được đặt sau khóa độc quyền mà peer cần.

No commit title was supplied. Suggested: `feat: add idempotent order checkout with stock reservations`.


## Milestone 13 — order reads, cancellation/release and requested Admin fulfillment (2026-11-05)

Initial Git status was clean (`main...origin/main`). Read root AGENTS.md, current specification/architecture/status/ADRs, package/SDK/tool pins, Order/checkout/inventory/audit models, all four deployed migrations, controllers/auth/HTTP contracts and the actual PostgreSQL harness/tests before editing. Only root AGENTS.md exists. No agents, new package, commit, push, deployment, history rewrite or developer-data deletion was used. The current request brings Admin 19 endpoints forward alongside 13; confirmation/consumption remains later, with no production confirmation/test route. Reported the retained expiry contract and history rename before implementation; no behavioral contradiction with the spec was found.

### Acceptance and behavior

| Criterion | Result and evidence |
| --- | --- |
| Customer list/detail/pagination/ownership | Complete: owner predicate in SQL before count/page/detail, page 1/size 20 defaults, size <=100, unique CreatedAt DESC/Id DESC, exact persisted status filter, other/missing 404, no owner/header/query spoofing |
| Admin list/detail and permissions | Complete: AdminOnly, optional status/customer filter, missing remains 404; Admin-only cannot cancel through Customer paths, dual-role non-owner cannot cancel another order |
| Effective expiry without read effects | Complete: no-store read DTOs expose fixed deadline/server observedAt/effectiveStatus/isExpired/endpoint-specific canCancel; read-only Repeatable Read transactions keep page/count and detail/items/history consistent; GET performs no expiry/release write |
| Central transitions and actor audit | Complete: OrderTransitions permits only the three forward Admin edges; Order methods compare version/apply state and produce OrderStatusHistory with from/to/version/actor/time/reason; creation fact preserved |
| Pending-only Customer cancellation | Complete: owner row lock precedes inventory, PendingPayment before deadline -> Cancelled; paid/fulfillment states reject with 409; already Cancelled repeats 200 with no new effects even after deadline |
| Global expiry boundary | Complete: now>=ExpiresAt after acquiring Order FOR UPDATE -> Expired/release/history/event commit, then 409 order_expired outside the transaction; repeat Expired stays 409 without effects; before/equal/after microsecond tests and real held-order-lock/fake-time test pass |
| PostgreSQL transition serialization | Complete: same Order FOR UPDATE strategy for Customer/Admin, expectedVersion and tracked-version predicate, sorted balances/reservations; all race participants count distinct contexts before the contested order SQL, never wait behind that lock |
| Release once / inventory isolation | Complete: Active -> Released, ReleasedAt/reason, Reserved decremented only, balance revision + linked ReleaseStock:v1/reservation operation; terminal SQL guards and unique effect; two hosts release each reservation once, unrelated orders/shared reservations/stock remain unchanged |
| Atomic audit/outbox/error handling | Complete: per-product release SQL, order/status history and one pending versioned event commit together; outbox faults after two releases roll everything back and permit safe retry; unknown failure remains logged/sanitized 500, known SQLSTATE classification retained for mutations |
| Admin forward fulfillment | Complete: Confirmed -> Processing -> Shipped -> Completed only, exact target name/expectedVersion/optional bounded reason, invalid/skipped/reversed/repeated/stale requests fail; concurrent same-version requests yield one success; no inventory/refund mutation |
| Data-preserving migration | Complete: reviewed ALTER TABLE/constraint/index/trigger rename replaces scaffolded history DROP/CREATE; legacy row actor/time/reason/version retained, creation actor kind backfilled without UPDATE, append-only and terminal protection tested; no old migration modified |
| Regression / independent unit suite | Complete: 90 unit + 235 integration/HTTP = 325 passed, 0 failed/skipped; 162 actual PostgreSQL cases (24 new), 163 service-free cases; five real migrations per fixture and upgrade regression |

Luồng chính: các GET lọc owner trong SQL rồi lấy count/page hoặc detail/items/history trong snapshot chỉ đọc. Backend tính expiry từ TimeProvider và deadline bất biến để frontend hiển thị hiệu lực, nhưng không tự ghi Expired khi đọc. Khi hủy, service mở transaction và khóa Order theo cả id lẫn owner trước, sau đó mới lấy thời gian quyết định. Nếu chưa hết hạn thì Cancelled; đúng/sau hạn thì Expired. Service khóa balance/reservation theo product ID, release Reserved đúng một lần, lưu movement, OrderStatusHistory và outbox, rồi commit. Nhánh Expired chỉ trả 409 sau commit; lỗi audit/outbox rollback cả các release SQL đã thực hiện.

Admin dùng cùng khóa Order, đối chiếu expectedVersion và graph tập trung. Chỉ ba cạnh Confirmed -> Processing -> Shipped -> Completed được chấp nhận; service tăng version, ghi actor Admin/time/reason/history/outbox và commit, không thay đổi tồn kho. Test seed Confirmed/history trong database riêng để mô phỏng writer thanh toán tương lai; đây không phải endpoint xác nhận thanh toán và không chứng minh capture/consume.

### Important files and migration

- `src/OrderFlow.Api/Ordering/OrdersController.cs`, `AdminOrdersController.cs`, `Program.cs`: thin Customer/Admin routes and scoped services, existing cookie/CSRF/role enforcement retained.
- `src/OrderFlow.Application/Modules/Ordering/OrderTransitions.cs`, `Order.cs`: central graph, cancellation boundary/duplicate semantics, version/actor changes and renamed OrderStatusHistory entity.
- `OrderReadService.cs`, `OrderReadContracts.cs`, `OrderViews.cs`: owner/filter/paging, read-only snapshots and effective-expiry DTOs separate from stored checkout bytes.
- `OrderLifecycleService.cs`, `OrderLocking.cs`, `OrderDatabase.cs`, `OrderEvents.cs`: order-first PostgreSQL coordination, timeout/classification/event helpers shared with checkout, atomic release and forward fulfillment.
- `Modules/Inventory/InventoryReservation.cs`, `InventoryBalance.cs`, `StockMovement.cs`, persistence configurations: terminal release metadata/guard, Reserved-only release, independent unique release effect and history mappings/indexes.
- **New migration 20261105084550_AddOrderManagement** plus generated designer/model snapshot. Renames ordering.OrderHistory to OrderStatusHistory without deleting data, adds FromStatus/ActorKind, reservation ReleasedAt/ReleaseReason, release checks/terminal function and list indexes. It deliberately refuses Down rather than discard audit metadata. All four earlier migrations are unchanged. Only owned test databases were migrated, never the configured developer/production database.
- `tests/OrderFlow.UnitTests/OrderLifecycleTests.cs`: 10 pure graph/boundary/actor/version/status/release-once tests; no external-service dependence.
- `tests/OrderFlow.IntegrationTests/Ordering/OrderManagement*`, `AdminOrderManagementTests.cs`: 24 actual PostgreSQL page/HTTP/role/CSRF/transition/boundary/race/fault/upgrade cases. Existing checkout tests now reference the preserved renamed audit store; persistence migration count is five.
- `docs/orders.md`, ADR-016 and active spec/architecture/status/README/testing/persistence/authorization/inventory/checkout guides document delivery scope, transaction walkthrough, forward migration and remaining work.

### Exact checks and results

Tooling unchanged: SDK 10.0.114; ASP.NET/EF/Identity/dotnet-ef 10.0.14; Npgsql 10.0.3; Testcontainers.PostgreSql 4.16.0. Used writable DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache and telemetry opt-out. Build/TRX/SQL output is under `/tmp/orderflow-orders`; no real credentials are printed/tracked. Official PostgreSQL locks/ALTER TABLE/isolation and Microsoft EF pagination docs were consulted. Package versions/lockfiles remain unchanged.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-orders/artifacts` — exit 0, all five projects. `dotnet --version` — 10.0.114.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-orders/artifacts` — final exit 0, **0 warnings/errors**. Initial focused test compilation flagged xUnit2013 on a collection-size assertion; replaced with Assert.Single, no suppression.
3. `dotnet ef migrations add AddOrderManagement --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with matching ArtifactsPath — exit 0. Scaffold warned about data loss from treating the renamed history entity as DROP/CREATE. Replaced that operation with reviewed ALTER TABLE/constraint/index/trigger renames BEFORE execution. Added release terminal protection and explicit forward-only downgrade refusal. Real per-case/upgrade PostgreSQL tests then applied the reviewed migration successfully.
4. `dotnet test OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-orders/artifacts --filter 'FullyQualifiedName~OrderLifecycleTests|FullyQualifiedName~OrderManagement' --logger 'trx;LogFilePrefix=order-management-focused' --results-directory /tmp/orderflow-orders/results` — final exit 0: **10 unit + 24 integration passed**, no failure/skip. TRX order-management-focused_net10.0_20261105155309.trx and order-management-focused_net10.0_20261105155340.trx. Includes row-preserving legacy upgrade and outbox-failure rollback of actual prior release SQL, not a fake provider or sleeps.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-orders/artifacts --logger 'trx;LogFilePrefix=orders-full' --results-directory /tmp/orderflow-orders/results` — exit 0: **90 unit + 235 integration/HTTP = 325 passed, 0 failed/skipped**. TRX orders-full_net10.0_20261105155904.trx and orders-full_net10.0_20261105160027.trx. Includes 162 PostgreSQL cases, all five actual migrations and existing checkout/Catalog/Inventory/Identity/policy/harness regressions. Confirmed fulfillment still succeeds after the former payment deadline; paid cancellation remains rejected.
6. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-orders/artifacts --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=orders-service-free' --results-directory /tmp/orderflow-orders/results` — exit 0: **90 + 73 = 163 passed, 0 failed/skipped**, no PostgreSQL fixture/container; orders-service-free_net10.0_20261105160721.trx and orders-service-free_net10.0_20261105160723.trx.
7. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with matching ArtifactsPath — exit 0/no model changes. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-orders/migrations.sql` — exit 0. Design-time configuration used no password and made no database connection. No destructive Down was executed.
8. Read-only Docker inventory compared the 4 container IDs recorded as created in these order-management TRX runs — **0 remained**. No unrelated resource was stopped/deleted. `git diff --check` — exit 0. `python3 /tmp/orderflow-harness/check-docs.py` — exit 0: 30 Markdown files, 269 valid local file links, balanced fences and consistent tables; remote URLs/anchor targets are outside this structural check. SDK/package/tool pins and package lockfiles remain unchanged.

### Manual verification, trade-offs and remaining work

Follow [orders.md](orders.md) for restore/build, dedicated migration/no-drift, full/filtered tests and the Customer/Admin HTTP walkthrough. With real cookie/CSRF sessions, create pending orders for two Customers; verify own count/page/detail, other-owner 404 and Admin all-order listing. Cancel a pending own order and repeat, inspect unchanged OnHand/remaining shared reservations and a single status/release event. Missing CSRF/wrong role must fail. Use deterministic fake-time tests for equality/lock waits, without manual sleeps. Admin PendingPayment -> Processing/Confirmed must fail; valid fulfillment requires an existing Confirmed order from the later verified-payment workflow, which only owned integration fixtures simulate today. No production seed/bypass endpoint was added.

Trade-offs: offset pages may shift between separate requests; read-only Repeatable Read adds transaction overhead but avoids count/history inconsistency and read writes. Per-product saves add roundtrips while retaining one commit. The audit table rename needs coordinated application/schema deployment because old binaries query the old name; the forward-only migration preserves records rather than offer a lossy downgrade. Stored checkout bytes remain immutable and do not acquire newer read metadata. Clocks require synchronization.

Remaining/unverified: automatic expiry/dispatch worker, verified payment confirmation/consumption, late-capture compensation/refunds and UI remain future scope. No paid-order cancellation/refund UX was introduced. Fulfillment fixtures model only the later Confirmed state/history, not capture/stock consumption; the full verified payment path still needs 18/19 integration tests. OS-process crash/uncertain-commit and real elapsed timeout/deadlock/serialization fault suites, least-privilege runtime roles and replica/shared-key deployment remain later work. Current two-host cancellation races use independent hosts/contexts in one test process. Release after commit is safe to retry; Admin uncertain results require refetching state/version, not inventing the next step.

### Interview preparation and suggested commit

1. **Vì sao khóa Order trước inventory và đặt barrier trước câu khóa Order?** Hiểu serialization cùng đơn, thứ tự khóa nhiều sản phẩm và deadlock giả khi request thắng giữ Order trong lúc barrier đòi request thua tới inventory.
2. **Vì sao expired cancellation trả 409 nhưng vẫn commit?** Hiểu deadline equality và locked decision time, expired release/history/outbox là kết quả bền vững; ném exception trước commit sẽ rollback sai và giữ tồn vô hạn.
3. **GET có nên tự sửa Expired không, và expectedVersion còn cần khi đã có row lock?** Hiểu read-only effective expiry tránh side effects, còn expectedVersion bảo vệ ý định Admin từ màn hình cũ dù SQL đã serialize các request.

No commit title was supplied. Suggested: `feat: add order management and serialized lifecycle transitions`.


## Milestone 14 expiry slice — 2026-11-06

### Inspection, scope and decisions

User explicitly requested the reservation/order expiry worker, not messaging dispatch. Initial git status was clean on main. Read root AGENTS.md, spec/architecture/status/ADRs, existing Worker composition/seed path, lifecycle/cancellation/domain rules, shared context/mappings, five migrations, pinned SDK/packages/locks and real-PostgreSQL harness/race tests before editing. No behavioral contradiction with ADR-003/016 was found. Roadmap 14 remains partly pending: this delivers expiry only. No agents, commit, push, deployment, developer database migration/reset, real credential fixture or unrelated package/feature was introduced.

ADR-017 records one due order per transaction via FOR UPDATE SKIP LOCKED, one fresh DI scope/context per attempt and bounded passes. Existing cancellation release SQL is extracted/reused instead of duplicated. System history uses null human ID; customer-triggered expiry retains Customer actor. Already Released reservation facts are preserved, Active quantities release once, and Consumed/missing/mismatched pending-order facts fail visibly. Generic Host handles unexpected worker errors by logging/stopping; known PostgreSQL contention is counted/logged and ends the pass until the next delayed iteration.

### Acceptance

| Criterion | Result / evidence |
| --- | --- |
| Separate worker + callable iteration | Complete: OrderExpiryWorker only in Worker; OrderExpiryIteration can run without scheduler; async scope/context per order; seed explicitly suppresses jobs |
| Due eligibility / injected time | Complete: PendingPayment and ExpiresAt <= normalized UTC; locked domain recheck; -1/0/+1 microsecond tests and other-status/not-yet-due isolation |
| PostgreSQL coordination and bounded work | Complete: parameterized ordered LIMIT 1 FOR UPDATE SKIP LOCKED; existing due index; 1–100 attempts/pass; each order commits separately; local lock/statement timeouts and cancellation |
| Atomic expiry/release/history/outbox | Complete: shared ordered balance/reservation release; real per-product SQL flushes stay in the order transaction; System Expired history + ordering.order-expired event/version commit together |
| Release exactly once / invariants | Complete for tested database effects: repeated passes, two independent hosts/shared products, already Released facts, cancellation overlap; fresh DB asserts unique releases, ledger/Active sums, OnHand unchanged and 0 <= Reserved <= OnHand |
| Cancellation competition | Complete: both contenders reach pre-order SQL gate; one release transaction. Separate deterministic test pauses an unexpired cancellation after its locked decision; worker skips it at deadline, and cancellation commits Cancelled once |
| Rollback / restart recovery | Complete for graceful/in-process host recovery: deliberate outbox failure after two release SQL writes; stop scheduled worker mid-release; fresh host/process iteration discovers remaining due data; batch-size-one backlog resumes |
| Prompt stop / polling control | Complete: token flows into DB and pauses; real scheduler test stops inside release transaction, another cancels an observed 300-second delay without sleeping through it |
| Configuration / test independence | Complete: bounds validated on startup; explicit loop-off service-free smoke/seed mode; no API scheduler or test endpoint; unit/service-free tests have no external service dependency |
| Messaging/payment scope | Remaining: dispatch/inbox, broker/consumer processing, payment settlement/consumption/refunds; expiry only stores Pending outbox intentions |

### Files and migration

- Application/Modules/Ordering: OrderExpiryProcessor, OrderReservationRelease, Order.Expire/OrderTransitions.CanExpire; cancellation reuses release helper. OrderDatabase contention classification is shared with Worker. History/read DTOs allow System/null user.
- Application/Modules/Inventory: System expiry Release movement, unchanged OnHand and terminal reservation semantics; mappings extend actor checks.
- Worker: OrderExpiryIteration, OrderExpiryWorker, OrderExpiryOptions, composition/configuration and seed-mode job suppression.
- Migration **20261106081044_AddSystemOrderExpiry**, generated designer/model snapshot: nullable history ActorUserId, System actor shape checks; preserves existing rows/FKs/triggers/indexes. Generated downgrade would fabricate empty UUIDs; replaced with explicit refusal before any downgrade execution. Old history-reader binaries require coordinated replacement before System writes. No due-work table or service account.
- Tests: OrderExpiryTests (14 PostgreSQL cases), WorkerCompositionTests (5 service-free cases), 4 additional domain cases plus smoke/migration-count adjustment. Existing lifecycle HTTP/SQL/security tests remain intact.
- Runbook [expiry-worker.md](expiry-worker.md), [ADR-017](adr/0017-order-expiry-worker.md), README/architecture/spec/order/persistence/testing and boundary docs updated.

### Exact checks and results

SDK remains 10.0.114; .NET/ASP.NET/EF/Identity/Hosting/dotnet-ef 10.0.14; Npgsql 10.0.3; Testcontainers.PostgreSql 4.16.0. Package/SDK/project/lockfile graph unchanged. Used DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache and telemetry opt-out. All artifacts/logs/TRX/generated SQL are under /tmp/orderflow-expiry (logs also /tmp/orderflow-expiry-*.log). PostgreSQL actually ran through Testcontainers, with generated private fixture credentials; no EF InMemory/SQLite substitute.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-expiry/artifacts` — exit 0/all five projects; no dependency changes. Initial sandbox restore/build exited 1 without actionable compiler diagnostics; approved execution outside the sandbox succeeded.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-expiry/artifacts` — exit 0, 0 warnings/errors. Focused test commands subsequently rebuilt the final migration/tests successfully.
3. `dotnet ef migrations add AddSystemOrderExpiry --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with matching `ArtifactsPath=/tmp/orderflow-expiry/artifacts` — exit 0. The initial metadata attempt used the default artifacts path and failed NETSDK1004; setting the matching environment path resolved it. Design-only connection has no password and does not contact a database. Reviewed Up preserves data; Down refuses destructive/null-actor conversion. Tests execute actual Up migrations in owned databases only.
4. `dotnet test OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-expiry/artifacts --filter 'FullyQualifiedName~OrderExpiry|FullyQualifiedName~WorkerComposition|FullyQualifiedName~OrderLifecycle' --logger 'trx;LogFilePrefix=expiry-focused-final' --results-directory /tmp/orderflow-expiry/results` — exit 0, **14 unit + 19 integration = 33 passed**, 0 failed/skipped. TRX expiry-focused-final_net10.0_20261106151335.trx and *_20261106151355.trx. Includes all 14 real-PostgreSQL expiry cases and 5 service-free Worker composition cases.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-expiry/artifacts --logger 'trx;LogFilePrefix=expiry-full' --results-directory /tmp/orderflow-expiry/results` — exit 0, **94 unit + 254 integration = 348 passed**, 0 failed/skipped. Includes 176 actual PostgreSQL tests; TRX expiry-full_net10.0_20261106151414.trx and *_20261106151548.trx.
6. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-expiry/artifacts --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=expiry-service-free' --results-directory /tmp/orderflow-expiry/results` — exit 0, **94 + 78 = 172 passed**, 0 failed/skipped, no PostgreSQL fixture required. TRX expiry-service-free_net10.0_20261106151700.trx and *_20261106151702.trx.
7. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` and `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-expiry/migrations.sql`, with matching ArtifactsPath — both exit 0/no model drift; reviewed additive SQL generated without applying it to a developer database.
8. `git diff --check` and the local Markdown link/fence/table checker — passed. Markdown checker: 32 files, 289 local links, balanced fences/consistent tables. Final read-only Docker inventory after the boundary run found zero remaining test containers; no user containers were stopped/deleted.

9. After strengthening only the HTTP assertions, `dotnet test tests/OrderFlow.IntegrationTests -c Release --no-restore --artifacts-path /tmp/orderflow-expiry/artifacts --filter 'FullyQualifiedName~Deadline_uses_injected_UTC_microsecond_boundary' --logger 'trx;LogFilePrefix=expiry-http-boundary' --results-directory /tmp/orderflow-expiry/results` — exit 0, **3/3 passed**. Verifies current HTTP status/version/effective expiry and System/null actor JSON. TRX expiry-http-boundary_net10.0_20261106151918.trx. No production code changed after the full suite.

### Manual verification, trade-offs and interview preparation

See [expiry walkthrough](expiry-worker.md) for migration/private configuration, API checkout/current-read flow and two-worker commands. Run `dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile` after explicit migration; normal processing requires PostgreSQL. Seed commands never start expiry. Set OrderExpiry__Enabled=false explicitly for idle composition only. Automated exact-boundary commands require Docker by default, or private ORDERFLOW_TEST_POSTGRES pointing to actual PostgreSQL; unavailable prerequisites fail honestly. All unit/service-free checks remain independent.

Luồng chính: Worker gọi một lượt có giới hạn; mỗi order có scope/context/transaction riêng. PostgreSQL chọn và khóa order PendingPayment đến hạn, bỏ qua hàng đang bị khóa. Domain kiểm tra lại deadline rồi chuyển Expired. Cùng transaction đó giải phóng phần giữ hàng còn Active theo thứ tự product ID, ghi movement/history/outbox rồi commit. Trước commit mà lỗi/dừng thì rollback; khởi động lại tìm lại dữ liệu PendingPayment còn đến hạn. API GET chỉ đọc và hiển thị effective expiry, không chạy batch xử lý.

Trade-offs/remaining: polling retains stock conservatively until a pass commits; clocks need synchronization. SKIP LOCKED may return no available row while due work is held, so later polling is necessary. One-order transactions add roundtrips but bound locks/rollback. Consumed/missing/mismatched data or unknown dependency failures stop the worker visibly for investigation; known contention waits for the next poll, not a hot retry. No claim of broker delivery or exactly-once external effects. Separate hosts are tested in one test process; OS kill/network loss/uncertain commit, real elapsed lock-timeout/deadlock injection, payment races, readiness/lag monitoring, least-privilege roles and deployment remain unverified/later scope.

Three short interview questions (understand these before payment work):

1. Why SKIP LOCKED instead of a process lock? PostgreSQL coordinates API/independent workers and lets peers skip owned orders; local locks cannot coordinate processes.
2. Why order-before-inventory and one transaction per order? Matching cancellation/payment lock order avoids cycles, while per-order commit limits backlog lock duration and rollback scope.
3. What happens if the worker stops after SQL release but before outbox commit? The transaction rolls back; a later pass retries durable PendingPayment data. A committed Expired order is ineligible, and unique effects/terminal guards protect repeated processing.

Suggested commit title (adjusted to the explicitly requested partial milestone): `feat: add bounded PostgreSQL order expiry worker`.


## Payment initiation and simulator — 15 / submission foundations of 16 (2026-11-06)

### Inspection and scope clarification

Initial git status was clean on main. Read AGENTS.md, specification/architecture/status/ADRs, existing order/expiry/cancellation/idempotency services, API auth/CSRF/error composition, Worker/seed path, six migrations, pinned SDK/packages/lockfiles and PostgreSQL harness/test seams before editing. No agents, commits, pushes, deployments, developer database migrations/data resets or real credentials were used.

Two contradictions were reported before implementing dependent behavior: the latest request prefers an independent simulator host while previous architecture co-hosted its adapter; ADR-018 supersedes that detail. The request refers to deferred callback settlement as milestone 13, but repo 13 already implements order management and receipt/settlement are 17/18. Preserve IDs and defer those features. Requested scope implements 15 and provider/submission/reconciliation foundations of 16, not the outstanding outbox/inbox part of 14. There is no need to pretend a broker/dispatcher prerequisite is implemented: payment uses its own durable intention queue.

### Acceptance and main decisions

| Criterion | Result / evidence |
| --- | --- |
| IPaymentGateway boundary | Complete: submission, status lookup, provider full refund and refund lookup; bounded HTTP response/timeout, redirects/retries disabled, machine credentials separate from cookies |
| Independent controllable provider | Complete: optional OrderFlow.Simulator Kestrel host, own configured PostgreSQL connection/transactions/no merchant FKs; tests use second owned database and random loopback port |
| Safe simulator controls | Complete: explicit Development/Testing enablement, no Production/Staging, loopback URLs only, reject Kestrel endpoint overrides, three independent external secrets; no simulator routes in API |
| Stable provider identities/idempotency | Complete: persisted request/transaction/capture/event/refund IDs; PostgreSQL advisory locks and unique constraints; same key/body replays, changes conflict; capture evidence cannot regress |
| Scenarios / signed callback generation | Complete: Pending(default), Succeeded, Failed, timeout before accept, timeout after committed capture, mismatched amount; stable raw callback body/event ID + fresh timestamp HMAC; control credential required |
| Owner initiation / server money | Complete: Customer cookie/CSRF + owner SQL, empty body/unknown-field rejection, immutable order USD amount, deadline/status under order lock; expired new requests commit release/history/outbox then 409 |
| One unresolved attempt / client replay | Complete: partial unique active OrderId; same/different client-key races produce one attempt; original accepted bytes/status/Location persist; failed replacement requires new key and eligible order |
| Committed intention before external call | Complete: endpoint commits Queued + client snapshot and returns 202; processor claim commits/disposes its scope, calls provider, stores observation in a fresh token-fenced transaction; independent NOWAIT probe proves locks are free at gateway invocation |
| Timeout/mismatch/reconciliation | Complete: timeout is Unknown, never Failed; lookup/possible resubmit use the original provider key. Mismatch quarantines unresolved evidence. Eight deliveries with bounded delays; claim #8 marks NeedsAttention durably even if process dies during its call |
| Provider success / frontend boundary | Complete for scope: observed success remains unresolved Pending/settlement_pending; no order confirmation, inventory consumption or browser success authority. Original client snapshot unchanged; current attempt read exposes latest observation |
| Recovery and concurrency | Complete for tests: real HTTP timeout/provider-host restart, stale lease cannot overwrite newer success, merchant observation failure after provider capture recovers after lease expiry; no duplicate provider capture/attempt |
| Refund scope | Provider-only idempotent full refund contract/result implemented and tested. No merchant refund intentions, compensation worker, paid-order cancellation/refund UX |
| Deferred work | Callback delivery/receipt/signature verification/inbox/settlement, capture allocation/consumption/late compensation, operator replay tooling, remaining messaging/projections/UI/deployment |

### Files / migration / dependencies

- Application/Modules/Payments: PaymentAttempt, PaymentInitiationService/DTOs, IPaymentGateway contracts, SimulatorPaymentGateway HTTP adapter, callable PaymentProcessingIteration and Simulation provider entities/services/callback signer.
- API/Payments/PaymentAttemptsController: POST own order payment-attempts and GET current own attempt; global cookie/CSRF/strict JSON policies retained. Program registration and OpenAPI description updated.
- Worker/PaymentWorker + WorkerHost/appsettings: optional dev/test-only simulator transport, 20 sequential attempts/pass, cancellable 5s scheduling, separate scopes/phases and seed suppression. Payment processing defaults off; expiry remains independently enabled.
- New optional src/OrderFlow.Simulator host, safe disabled sample settings, generated packages.lock.json; solution/test project reference updated. Core API/Worker/Application production boundaries remain; simulator cannot start in Production.
- Migration **20261106084012_AddPaymentInitiationAndSimulator**, designer and snapshot: four additive tables, active/due/provider identity indexes, numeric/lease/state checks, restrictive FKs, SQL immutable attempt/provider/capture/refund guards. Existing data retained; Down refuses identity/data loss. No developer database updated.
- PaymentAttemptTests adds 10 meaningful service-free domain cases; PaymentInitiationTests 12 PostgreSQL cases, PaymentGatewayTests 7, PaymentPersistenceTests 5, SimulatorCompositionTests 6 service-free cases, plus 3 production-route absence cases and migration-count adjustment.
- [Payment walkthrough/runbook](payments.md), [ADR-018](adr/0018-payment-initiation-and-simulator.md), architecture/spec/README/testing/persistence/boundary docs updated.

SDK remains 10.0.114, ASP.NET/.NET/EF/Identity/Hosting/dotnet-ef 10.0.14, Npgsql 10.0.3, Testcontainers.PostgreSql 4.16.0. No NuGet package/version was added/changed. New simulator lockfile contains existing transitive dependencies; IntegrationTests lockfile adds only the simulator project reference. Existing core/unit lockfiles and central pins are unchanged. Solution now has six projects.

### Exact verification

Used DOTNET_CLI_HOME=/tmp/orderflow-m02/dotnet-home, NUGET_PACKAGES=/tmp/orderflow-m02/packages, NUGET_HTTP_CACHE_PATH=/tmp/orderflow-m02/http-cache, telemetry opt-out and matching artifacts path. All binaries/TRX/generated SQL are under /tmp/orderflow-payments, logs /tmp/orderflow-payments-*.log. PostgreSQL actually ran in disposable Testcontainers; provider tests used true loopback Kestrel HTTP and separately owned provider DBs, not EF InMemory or in-memory gateway durability. No real secrets were printed/stored in deliverables. Official Microsoft HttpClient/HMAC/hosting and PostgreSQL partial-index documentation was consulted before version-sensitive implementation.

1. `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-payments/artifacts` — exit 0, generates only new project/reference lock information. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-payments/artifacts` — exit 0/all six projects. No package version changes.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-payments/artifacts` — final exit 0, **0 warnings/errors** (/tmp/orderflow-payments-build-final.log).
3. `dotnet ef migrations add AddPaymentInitiationAndSimulator --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with `ArtifactsPath=/tmp/orderflow-payments/artifacts` — exit 0. Reviewed additive schema and added immutable/monotonic SQL guards plus downgrade refusal. Actual Up migrations executed only on owned test DBs; no Down executed. Design-only configuration had no password and did not connect to a database.
4. `dotnet test OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-payments/artifacts --filter 'FullyQualifiedName~Payment|FullyQualifiedName~SimulatorComposition' --logger 'trx;LogFilePrefix=payments-final-focused' --results-directory /tmp/orderflow-payments/results` — exit 0, **9 unit + 30 integration passed**, 0 failed/skipped. TRX payments-final-focused_net10.0_20261106154823.trx and *_20261106154852.trx. Subsequent review added one crash-at-eighth-claim regression and durable attention fix; both included in final full/service-free runs below.
5. `dotnet test OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-payments/artifacts --logger 'trx;LogFilePrefix=payments-final' --results-directory /tmp/orderflow-payments/results` — final exit 0, **104 unit + 287 integration = 391 passed**, 0 failed/skipped, including **200 real PostgreSQL cases**. TRX payments-final_net10.0_20261106155649.trx and *_20261106155847.trx. Earlier full run passed 390 before adding the crash-limit case; final rerun validates the fix and bounded provider-host disposal.
6. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-payments/artifacts --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=payments-service-free-final' --results-directory /tmp/orderflow-payments/results` — exit 0, **104 + 87 = 191 passed**, 0 failed/skipped, no PostgreSQL/provider network services. TRX payments-service-free-final_net10.0_20261106155950.trx and *_20261106155952.trx.
7. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with matching ArtifactsPath — exit 0/no changes. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-payments/migrations.sql` — exit 0; reviewed generated SQL includes custom guards. No developer migration applied.
8. `git diff --check` and local Markdown link/fence/table verification passed. Final read-only Docker test-container inventory found no remaining containers; no user containers were stopped/deleted. Final documentation check: 34 Markdown files, 310 valid local links, balanced fences/consistent tables.

### Flow explanation, manual checks, trade-offs

Luồng chính: Customer gọi initiation bằng cookie/CSRF và client key, server khóa order để kiểm tra owner/deadline rồi ghi Queued attempt + response 202 trong một transaction. Worker claim một attempt, commit/hủy scope trước khi gọi gateway qua HTTP. Sau đó dùng scope mới, kiểm tra lease token rồi ghi observation. Timeout giữ Unknown và provider key cũ; success chỉ là observation chờ settlement, không đổi order thành Confirmed. Simulator lưu transaction/capture độc lập nên restart hoặc mất response vẫn query lại được cùng giao dịch.

Manual commands/configuration are in [payments](payments.md): migrate merchant (and optionally independent provider DB) explicitly, start restricted simulator with private machine/control/signing keys and Development environment, keep PaymentProcessing off while choosing a per-key scenario, then enable/start Worker with loopback BaseUrl/matching machine key. Use authenticated POST /api/orders/{id}/payment-attempts with empty body + CSRF + Idempotency-Key, GET its Location for current state, compare same-key response bytes, and use a new key only after definitive failure. Normal expiry stays independent. Full payment integration tests require Docker by default or private ORDERFLOW_TEST_POSTGRES to actual PostgreSQL; missing prerequisites fail honestly. Unit/service-free checks require neither.

Trade-offs/limitations: optional fourth dev/test host and secrets add setup; the common migration chain means standalone provider DBs include unused merchant tables. Polling adds lag; accepted intent may reach provider after expiry/cancellation, and future settlement must enforce deadline/compensation. Eight-delivery exhaustion remains unresolved/visible and blocks replacements; audited replay tooling is deferred. Callback envelopes are generated but not sent/received/settled. No merchant Capture/Refund compensation or real provider/production payment guarantee. Registration and callable processing/network phases are tested; OS-process kill/network partition/uncertain commit, extended hosted-loop operability, runtime DB least privilege and deployment readiness remain later verification. No exactly-once external side-effect claim.

Three interview questions (understand before signed settlement work):

1. Why commit the intention before calling the gateway? Restart/timeout can find the durable attempt and reuse the same provider key; no business lock is held over HTTP.
2. Why keep timeout Unknown? Provider may already have captured; another attempt/key could charge again. Query/retry the original provider identity.
3. Why both lease token and provider idempotency? Token fences stale database observations; provider key protects repeated external effects when a lease expires or a response is lost.

Suggested commit title (adjusted to delivered scope): `feat: add idempotent payment initiation and isolated simulator`.

## Signed payment callbacks and atomic settlement — 17 / allocation slice of 18 (2026-11-11)

### Scope and decisions

Implements the requested signed callbacks, durable PaymentEvents deduplication, capture facts, stock consumption/order confirmation and unique RefundPending intentions. The existing spec already requires separate receipt/processing and known USD amount mismatch compensation; these were reported before dependent implementation and followed. This is not the full roadmap 18 refund-delivery/reconciliation workflow or generic messaging dispatcher. [ADR-019](adr/0019-signed-payment-settlement.md) records the exact protocol, locks, database-only inbox consumer and scope. No package/SDK changes or new architecture layer; no commit/push/deploy or developer database migration.

### Acceptance delivered

| Criterion | Result |
| --- | --- |
| Provider authentication / A14 | Dedicated injected secret/key ID; exact raw bytes + canonical delivery timestamp HMAC-SHA256; fixed-time compare; five-minute skew with inclusive boundary; no cookie authority; explicit sole business CSRF exemption |
| Input bounds/schema | Declared/streaming bodies capped at 64 KiB; reject encoded bodies, malformed/duplicate/missing/unknown JSON, invalid identities/money/state; no paid flag |
| Durable receipt/dedup | PaymentEvents unique provider/event ID; preserve exact bytes + hash; atomic existing messaging inbox insert before 202; exact replay 202; changed authenticated bytes 409 + alert 1601 |
| Reference/currency/amount validation | Validate stored order/attempt/key/provider/transaction and capture identity; unknown/mismatched references/non-USD durably quarantined with Failed inbox/error; known positive USD amount mismatch captured and compensated in full, never confirmed |
| Capture / A10, A13 | Independent immutable PaymentCaptures, Captured attempt; monotonic success/failure/pending processing and submission lease fencing; one allocated capture/order; old Failed can later capture |
| Allocation / A07–A11, A16 | Order lock first, sample normalized DecisionAt; exact matching unexpired pending order consumes Active reservations, decrements both stock quantities, confirms, appends System history/order+payment outbox and completes inbox atomically |
| Expiry/cancellation / A11 | Receipt/provider time never overrides decision time; overdue pending expires/releases in same transaction; terminal orders never resurrect; Released/Consumed stay terminal; late/surplus captures create one full RefundPending/provider capture |
| Persistence guards | SQL immutable/terminal triggers, unique transaction/capture/allocation/refund identities, ConsumedAt check; deferred commit constraints require correct capture allocation or full matching compensation |
| Worker/recovery | Opt-in PaymentSettlement Worker; 20 sequential events/pass, fresh context per event; no HTTP in transaction; classified contention has durable five-second scheduling/eight-attempt limit, then visible Failed inbox; unexpected errors propagate |
| Compatibility | Browser CSRF/ownership and original initiation replay retained; new initiation reuses only Queued/Pending/Unknown, including replacement after mismatched captured payment |
| Tests | Real PostgreSQL receipt/cancel/expiry/settlement races with distinct-context gates; boundary/held-lock/frozen-decision tests; callback/submission response race; stock/ledger/capture/refund reconciliation; receipt/inbox and executed-consumption/outbox rollback; SQL evidence/compensation guards |

### Luồng chính (Vietnamese)

API đọc tối đa 64 KiB byte gốc, xác thực HMAC cùng timestamp bằng secret riêng rồi mới parse schema. Một transaction INSERT PaymentEvents theo provider/event ID và ghi inbox; duplicate giống byte trả 202, khác byte trả 409 và log cảnh báo. 202 chỉ xác nhận đã lưu bền vững.

Worker dùng scope/DbContext mới cho mỗi event. Transaction khóa Order trước và lấy DecisionAt UTC, sau đó khóa danh tính provider/attempt/receipt/inbox để kiểm tra lại. Capture USD đúng tiền, đúng attempt và còn hạn tiêu thụ từng Active reservation theo ProductId: giảm cả OnHand/Reserved, tạo ledger, xác nhận đơn, ghi history System/outbox và đánh dấu inbox trong cùng commit. Nếu đơn quá hạn, release chỉ Active và chuyển Expired; capture vẫn là sự thật tài chính độc lập, có một RefundPending với số tiền thực thu. Cancelled/Expired/đơn đã thanh toán không sống lại. Callback thất bại đến muộn không xóa Captured; callback kết thúc lease để response gateway cũ không ghi đè. RefundPending chưa thực hiện HTTP hoàn tiền.

### Important files and migration

- API/Payments/PaymentCallbackController + PaymentCallbackSettings: bounded transport, headers/HMAC/skew, strict schema, CSRF exemption and injected private configuration; API Program/appsettings/OpenAPI updated.
- Application/Modules/Payments/PaymentCallback, PaymentCallbackReceipt, PaymentEvent and PaymentSettlementProcessor: protocol, exact-byte receipt/inbox, independent capture/refund intentions, order-first database settlement and bounded durable contention handling.
- Existing PaymentAttempt/PaymentInitiationService: monotonic Captured/Failed handling, submission response fencing and explicit unresolved-state reuse. Existing InventoryBalance/Reservation/StockMovement and Order add consumption/System confirmation rules.
- PaymentSettlementConfiguration, shared context and existing inventory/ordering/payment mappings: uniqueness/FKs/checks; Worker/PaymentSettlementWorker and WorkerHost/appsettings: opt-in fresh-scope polling independent of gateway.
- Migration **20261111040724_AddSignedPaymentSettlement**, designer/snapshot: additive PaymentEvents/PaymentCaptures/Refunds and ConsumedAt, revised state/history constraints, immutable/terminal and deferred disposition guards. Down refuses evidence loss. Applied only to owned test databases.
- PaymentCallbackTests, PaymentSettlementDomainTests, composition/migration-count regressions; [callback runbook](payment-callbacks.md), [payment guide](payments.md), spec/architecture/ADR/testing/README updated.

### Exact verification evidence

All final commands use `DOTNET_CLI_HOME=/tmp/orderflow-callbacks/dotnet-home` and `NUGET_PACKAGES=/tmp/orderflow-callbacks/packages`. No package/SDK pins or lockfiles changed. The first sandbox restore failed with NU1301/permission denied to api.nuget.org; approved network restore to /tmp resolved it. Tests used disposable Testcontainers PostgreSQL, not a developer database or EF InMemory. Docker/test-host execution required approved sandbox access; all verification below completed successfully.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-callbacks/artifacts --disable-parallel -m:1` — exit 0/all six projects; `/tmp/orderflow-callbacks-restore.log`. `dotnet tool restore` — exit 0, pinned dotnet-ef 10.0.14; `/tmp/orderflow-callbacks-tools.log`.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-callbacks/artifacts -m:1` — final exit 0, **0 warnings/errors**, `/tmp/orderflow-callbacks-build-final.log`.
3. `dotnet ef migrations add AddSignedPaymentSettlement --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with `ArtifactsPath=/tmp/orderflow-callbacks/artifacts` and a nonconnecting design-only connection — exit 0. Reviewed generated additive changes, added SQL immutable/terminal/deferred compensation guards, and replaced destructive Down with refusal. No developer database was updated; actual Up executed only in owned test DBs.
4. Initial focused `dotnet test tests/OrderFlow.IntegrationTests/OrderFlow.IntegrationTests.csproj -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-callbacks/artifacts -m:1 --filter 'FullyQualifiedName~PaymentCallbackTests' --logger 'trx;LogFileName=callbacks-focused.trx' --results-directory /tmp/orderflow-callbacks/results` — exit 0, **24 passed**, no failed/skipped. Expanded `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-callbacks/artifacts -m:1 --filter 'FullyQualifiedName~PaymentCallbackTests|FullyQualifiedName~PaymentSettlementDomainTests' --logger 'trx;LogFilePrefix=callbacks-expanded' --results-directory /tmp/orderflow-callbacks/results` — exit 0, **30 PostgreSQL + 5 domain passed**, no failed/skipped. Subsequent acceptance additions are included in the full run below.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-callbacks/artifacts -m:1 --logger 'trx;LogFilePrefix=callbacks-full' --results-directory /tmp/orderflow-callbacks/results` — exit 0, **323 integration + 109 unit = 432 passed**, **0 failed/skipped**. Includes **235 actual PostgreSQL cases**, of which **35 are new callback/settlement cases**, plus five new domain cases and updated composition regressions. Evidence: `/tmp/orderflow-callbacks-full-tests.log`; TRX `callbacks-full_net10.0_20261111111936.trx` and `callbacks-full_net10.0_20261111111937.trx` under `/tmp/orderflow-callbacks/results`.
6. Final worker review removed the outer automatic contention loop: the processor already persists bounded per-event retries; a failure while recording retry metadata must propagate visibly. Rebuilt successfully, then `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-callbacks/artifacts -m:1 --filter 'Category!=PostgreSQL' --logger 'trx;LogFilePrefix=callbacks-service-free-final' --results-directory /tmp/orderflow-callbacks/results` — exit 0, **88 integration + 109 unit = 197 passed**, no failed/skipped, including final Worker composition. Evidence `/tmp/orderflow-callbacks-service-free.log` and `callbacks-service-free-final_net10.0_20261111112057.trx` / `...112058.trx`. No SQL/settlement changes followed the full PostgreSQL run.
7. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with matching ArtifactsPath/design connection — exit 0, **no model drift**; `/tmp/orderflow-callbacks-drift.log`. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-callbacks/migrations.sql` — exit 0; reviewed SQL includes deferred capture/refund disposition and immutable/terminal guards; `/tmp/orderflow-callbacks-script.log`. `git diff --check` — exit 0.

Not claimed: real provider/funds, merchant refund HTTP execution/reconciliation, automated callback delivery, deployed multi-process/OS-kill/network-partition/uncertain-commit testing or production key rotation. Callback/stock races use actual concurrent contexts/SQL and deterministic synchronization, not a claim about external exactly-once delivery.

### Manual verification and trade-offs

Follow [callback runbook](payment-callbacks.md): explicitly migrate; privately configure matching API PaymentCallbacks secret/key ID and simulator callback signing credential; enable PaymentSettlement Worker independently; obtain an operator-generated simulator envelope and POST exact Body bytes with the three documented headers. Check owner GET for current state, redeliver duplicates, tamper bytes, cancel before success and inspect RefundPending. Original payment initiation replay remains its initial accepted snapshot.

The simulator does not schedule callback HTTP delivery. Refund network execution/reconciliation, generic outbox dispatch, operator replay/UI and production provider hardening remain pending. Database-only inbox processing deliberately uses held row locks instead of introducing network leases. Multiple workers may discover the same event and recheck under locks, which trades throughput for simple rollback semantics. Failed/quarantined inbox rows remain visible for future audited reconciliation; no automatic unsafe reassociation, currency conversion or exactly-once external-effect claim. No developer data/volume was reset.

### Three interview questions

1. **Vì sao 202 chưa có nghĩa đã thanh toán thành công?** Hiểu durable receipt/inbox tách khỏi settlement, worker lag và crash recovery.
2. **Vì sao phải khóa Order trước và dùng DecisionAt thay vì occurredAt?** Hiểu điểm quyết định chung với cancellation/expiry, equality hết hạn và cách giữ quyết định khi chờ inventory.
3. **Vì sao Captured khác Confirmed, và dedup event chưa đủ?** Hiểu capture là tiền thực thu; event/capture/refund có danh tính riêng; tiền đến muộn/surplus phải có full-compensation intention và không tiêu thụ/release lần hai.

Suggested commit title (no title supplied; reflects delivered 17 / allocation slice of 18): `feat: add signed payment callbacks and atomic settlement`.

## Durable refund execution and payment reconciliation — 18 completion (2026-11-11)

### Scope and contract clarification

Implements the requested full late-capture compensation workflow using IPaymentGateway. No general customer refunds/returns, public refund-success endpoint, generic dispatcher or extra package/pattern is added. The latest requested Pending/Succeeded/NeedsReview refund model supersedes planned separate Unknown/Failed states: uncertain outcomes remain Pending with LastErrorCode/backoff/original key, blocked/exhausted work is NeedsReview. Provider lookup lacks event ID/occurrence time; that distinction was reported before extending capture evidence, and Source=ProviderLookup retains null provider fields rather than inventing a signed receipt/time. [ADR-020](adr/0020-refund-execution-and-payment-reconciliation.md) records these choices and extends ADR-019 while preserving its signature and stock rules.

### Acceptance delivered

| Criterion | Result |
| --- | --- |
| Stable compensation identity | Same provider/capture/FullCompensation unique intention, immutable refund key/capture/amount/currency; provider key never regenerated on timeout/restart |
| Durable state / scheduling | Pending/Succeeded/NeedsReview; delivery count, NextAttemptAt/UpdatedAt, lease token/deadline, provider refund ID, success time and sanitized error |
| Cross-worker claims / conditional completion | Ordered PostgreSQL FOR UPDATE SKIP LOCKED, one refund per short transaction, 60-second token lease; completion under fresh row lock plus EF lease predicate; stale workers cannot overwrite success/new claim |
| External I/O boundary | Claim commits/disposes scope before LookupRefund/Refund; fresh result scope; independent NOWAIT probe verifies no merchant refund transaction at both network operations |
| Lost result / restart | Always lookup original refund key first; if absent submit same key/body; real provider timeout-after-success and host restart recover same refund ID; local acknowledgement failure leaves claim for recovery |
| Bounded recovery | Maximum eight committed deliveries, 5/15/30/60/120/300/600/600 second backoff; eighth claim itself stores review; permanent rejection/mismatch goes to review without blind retries |
| Payment reconciliation | Lookup-only worker shares submission lease/counter; previously delivered Queued/Pending/Unknown plus settlement observations; missing/Pending backoff, definitive Failed, explicit exhausted/permanent/mismatch NeedsAttention |
| Shared settlement / correlation | Extracted PaymentSettlement used by callback and lookup; lock stored Order then sample decision time, bind provider identities, lock stored attempt and verify token; same reference/key/transaction/currency/server amount/capture checks and stock/expiry/compensation logic |
| Evidence / no resurrection | Source=ProviderLookup with null provider event/occurrence; callback facts/FK remain; exact deadline expires/releases and creates compensation; terminal orders and captured facts cannot regress |
| Worker lifecycle | Independent opt-in RefundProcessing/PaymentReconciliation, restricted shared simulator gateway config, 20 sequential items/pass, five-second cancellable polling, fresh phase scopes; seed mode suppresses loops |
| Simulator contract | Independent durable capture/key/full-refund identities, commit-before-response and lookup after restart; operator-only refund Succeeded/TimeoutBeforeAccept/TimeoutAfterSuccess/Rejected scenarios; actual guarantees are provider-dependent |
| Regression / migration | Populated prior-schema compensation preserves ID/key/amount/capture and initializes due metadata; SQL identity/success/review guards and no-delete/no-truncate protections; no developer DB migration or data reset |

### Luồng chính (Vietnamese)

RefundWorker lấy một refund đến hạn bằng SKIP LOCKED, cấp lease token và tăng số lượt rồi commit, giải phóng context trước khi gọi provider. Nó lookup bằng đúng ProviderRefundKey đã lưu; chỉ khi chưa có kết quả mới gửi full refund bằng cùng key/capture/amount/currency. Transaction mới khóa refund, đối chiếu token và response, rồi ghi Succeeded + outbox hoặc Pending/backoff/NeedsReview. Nếu provider đã hoàn tiền nhưng response hoặc local acknowledgement mất, worker sau tìm lại cùng refund thay vì tạo danh tính khác. Worker cũ trả response sau reclaim không thể ghi đè.

PaymentReconciliationWorker claim attempt bằng lease/counter dùng chung với submission và chỉ lookup provider. Sau HTTP, nó khóa Order từ dữ liệu đã lưu, lấy DecisionAt UTC, khóa identity/attempt và kiểm tra token. Kết quả đi qua đúng PaymentSettlement của callback: chỉ tiền/identity hợp lệ và đơn còn hạn mới consume/confirm; capture muộn hoặc sai tiền USD được ghi nhận và tạo một full-compensation intention. Callback thắng trước sẽ xóa token, nên response lookup cũ không làm lùi Captured. Không bịa provider event ID/occurredAt từ thời gian local. Failed lookup phải là thất bại dứt khoát; trường hợp chưa chắc chắn giữ Unknown và dừng ở NeedsAttention khi hết lượt.

### Important files and migration

- Application/Modules/Payments/PaymentRefund + RefundProcessingIteration: compensation domain state, stable request, backoff/review, claim/network/token-fenced acknowledgement and refund-succeeded outbox.
- PaymentReconciliationIteration + existing PaymentAttempt/PaymentProcessingIteration: shared attempt lease, lookup-only recovery, durable observations/review and permanent-error classification.
- PaymentSettlement extracted from PaymentSettlementProcessor; callback keeps its existing durable receipt/inbox protocol and shares all business validation/allocation with lookup. PaymentCapture adds explicit Source and nullable provider event/occurrence.
- SimulatorPaymentGateway/IPaymentGateway: classified retryable/permanent HTTP failures, original identity lookup/refund contract; provider SimulatorService/Host and ProviderRefundScenarios add controlled real timeout/rejection behavior without merchant dependencies.
- Worker/RefundWorker, PaymentReconciliationWorker, WorkerHost/appsettings: separate opt-in bounded loops and shared restricted gateway configuration; existing callback/submission/expiry behavior retained.
- Migration **20261111044314_AddRefundRecovery**, designer/snapshot: data-preserving RefundPending->Pending and timestamp backfill, delivery/outcome/lease metadata, due/provider-result indexes, immutable identity/terminal review/success guards, lookup provenance and simulator scenarios. Down refuses evidence loss; applied only to owned test DBs.
- PaymentRecoveryTests, RefundDomainTests, GatewayFailureClassificationTests and composition/legacy callback/migration-count updates; [recovery runbook](payment-recovery.md), [ADR-020](adr/0020-refund-execution-and-payment-reconciliation.md), current spec/architecture/callback/payment/persistence/testing/README documentation updated.

### Exact verification evidence

All commands use `DOTNET_CLI_HOME=/tmp/orderflow-callbacks/dotnet-home` and `NUGET_PACKAGES=/tmp/orderflow-callbacks/packages`; artifacts/logs/TRX/SQL are under /tmp/orderflow-recovery. The initial restricted restore failed NU1900 while fetching vulnerability metadata; approved network restore resolved it without disabling auditing. Package/SDK pins and lockfiles did not change. PostgreSQL integration used disposable Testcontainers databases and separate real simulator HTTP hosts; no InMemory/SQLite substitute, developer database migration or data reset.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-recovery/artifacts --disable-parallel -m:1` — exit 0/all six projects; `/tmp/orderflow-recovery-restore.log`.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-recovery/artifacts -m:1` — final exit 0, **0 warnings/errors**; `/tmp/orderflow-recovery-build-final.log`.
3. `dotnet ef migrations add AddRefundRecovery --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with `ArtifactsPath=/tmp/orderflow-recovery/artifacts` and a nonconnecting design-only connection — exit 0; `/tmp/orderflow-recovery-migration.log`. Reviewed scaffold, inserted legacy intention/timestamp backfill before new constraints, replaced the old whole-row immutable trigger with identity/terminal guards, and made Down refuse evidence loss. Actual Up and populated upgrade executed only in owned test DBs.
4. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-recovery/artifacts -m:1 --filter 'FullyQualifiedName~PaymentRecoveryTests|FullyQualifiedName~RefundDomainTests|FullyQualifiedName~PaymentCallbackTests' --logger 'trx;LogFilePrefix=recovery-focused' --results-directory /tmp/orderflow-recovery/results` — exit 0, **51 integration + 6 domain passed**, no failed/skipped; `/tmp/orderflow-recovery-focused.log`.
5. Expanded `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-recovery/artifacts -m:1 --filter 'FullyQualifiedName~PaymentRecoveryTests|FullyQualifiedName~RefundDomainTests|FullyQualifiedName~SimulatorCompositionTests' --logger 'trx;LogFilePrefix=recovery-expanded' --results-directory /tmp/orderflow-recovery/results` — exit 0, **28 integration + 6 domain passed**, no failed/skipped, including populated legacy upgrade, shutdown recovery and exact-expiry reconciliation; `/tmp/orderflow-recovery-expanded.log`.
6. Full pre-final-classification run `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-recovery/artifacts -m:1 --logger 'trx;LogFilePrefix=recovery-full' --results-directory /tmp/orderflow-recovery/results` — exit 0, **344 integration + 115 unit = 459 passed**, no failed/skipped; `/tmp/orderflow-recovery-full.log`.
7. Final review classified malformed/oversized protocol responses and unknown HTTP-request errors as review, with retries restricted to known transient HTTP/network categories. Added ten service-free classification cases (including actual HttpClient buffer-limit behavior), rebuilt, then `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-recovery/artifacts -m:1 --logger 'trx;LogFilePrefix=recovery-final' --results-directory /tmp/orderflow-recovery/results` — exit 0, **344 integration + 125 unit = 469 passed**, **0 failed/skipped**. This includes **19 new actual PostgreSQL recovery cases**, six refund-domain cases, ten transport-classification cases and two new composition cases. Evidence: `/tmp/orderflow-recovery-final.log`; TRX `/tmp/orderflow-recovery/results/recovery-final_net10.0_20261111121305.trx` and `recovery-final_net10.0_20261111121306.trx`.
8. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with matching ArtifactsPath/design-only connection — exit 0, **no model drift**, `/tmp/orderflow-recovery-drift.log`. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-recovery/migrations.sql` — exit 0; reviewed backfill/guards/provenance, `/tmp/orderflow-recovery-script.log`. No model/schema change followed those checks. `git diff --check` — exit 0.

Unverified limitations remain real-provider behavior/funds, deployed independent OS-process crashes/network partitions/uncertain database commit, production runtime roles/key rotation and audited operator replay. Provider host restart, intentional transport timeout, shutdown cancellation, local acknowledgement rollback, deterministic concurrent claims/callbacks and actual PostgreSQL stock/fund identity reconciliation were executed. No universal exactly-once external guarantee is claimed.

### Manual verification / provider-dependent limits

Follow [recovery runbook](payment-recovery.md): migrate merchant/provider explicitly, start restricted simulator with private keys, enable PaymentProcessing + PaymentReconciliation + RefundProcessing (and PaymentSettlement for callbacks), then checkout/initiate, cancel before capture handling, and inspect one Pending compensation. Set operator-only TimeoutAfterSuccess for its original provider refund key; inspect one provider refund despite local timeout, restart/await due lease and verify the same ID becomes Succeeded locally. Redeliver the late signed callback and reconcile stock/ledger/capture/refund identities. Inspect NeedsReview/NeedsAttention error/counter fields on rejection/exhaustion; never create an unrelated key to repair uncertainty.

Tests prove one effective refund per capture under the simulator's durable key/capture uniqueness and lookup-retention contracts, not external exactly-once execution. An actual provider must durably bind immutable request facts, retain keys/results through the recovery window, commit before acknowledgement, expose lookup after restart and make success monotonic. A provisional failure cannot be mapped to Failed; simulator's explicit Failed->Succeeded fault scenario requires a later verified callback after automatic failed-attempt polling stops. Real notifications or audited same-identity recovery are needed for that provider behavior.

No real money/provider, deployed OS-process kill/network partition/uncertain commit, audited operator replay tool, callback delivery scheduler, generic outbox publishing, customer refunds/returns or production readiness is claimed. Timeouts/injected local acknowledgement failures and separate HTTP-host restarts are tested within one test process, with actual PostgreSQL and independent provider persistence. No commit/push/deploy or developer data deletion.

### Three interview questions

1. **Vì sao timeout refund không được tạo key mới?** Hiểu provider có thể đã commit; lookup và retry phải dùng key/capture cũ để tránh hiệu ứng ngoài DB bị lặp.
2. **Lease token khác provider idempotency thế nào?** Token chặn worker cũ ghi đè trạng thái local; provider key/capture uniqueness mới bảo vệ tiền khi external request lặp sau crash/reclaim.
3. **Vì sao reconciliation phải gọi lại settlement chung?** Hiểu lookup không phải quyền bỏ qua order/attempt/amount/currency correlation, order-first decision time, expiry và compensation; provider timestamps không phục hồi tồn kho.

Suggested commit title (none supplied; matches requested scope): `feat: add durable refund execution and payment reconciliation`.


## RabbitMQ outbox publication — publisher slice of 14 (2026-11-12)

### Scope and contract clarification

The latest request connects existing events to RabbitMQ rather than implementing the originally planned broker-free local dispatcher. This contradiction was reported before dependent implementation. [ADR-021](adr/0021-rabbitmq-outbox-publication.md) supersedes producer completion waiting for each local consumer: Published now means successful mandatory routing plus broker confirmation only. Each future business consumer still requires its independent inbox/effects transaction before ACK. No notification/reporting handler, general event bus framework, payment/refund rule change, audited replay, API/Worker deployment or customer refund feature is introduced.

Repository instructions, current package locks/migrations/spec/architecture/ADRs and clean initial Git status were inspected. During work global.json was independently changed from SDK 10.0.114 to installed 10.0.115; that user change is preserved. This milestone adds only RabbitMQ.Client 7.2.2 and its generated Worker/Integration dependency lock changes. Broker 4.4.0-management digest was pulled/verified against official sources; EF/ASP.NET/Npgsql pins remain unchanged. Version-sensitive async confirm/return/concurrency behavior was checked against official 7.2.2 source and RabbitMQ documentation linked in ADR-021.

### Acceptance delivered

| Criterion | Evidence/behavior |
| --- | --- |
| Existing events/checkout boundary | Producers unchanged; independent publisher sees only committed rows; rollback creates no external delivery. Real authenticated checkout returns 201 during broker failure and preserves its event for later confirmed publication |
| Versioned stable envelope | EnvelopeVersion 1, EventId/type/schema/time, aggregate/version, correlation/causation and optional persisted producer W3C trace; payload is an object with unchanged money strings; same identity/body on retries |
| Bounded PostgreSQL claims | One row per short FOR UPDATE SKIP LOCKED transaction, 20 items/pass; unique owner/token, 60-second lease and separate bounded exhausted-lease cleanup; independent contexts reach the contested statement |
| Transaction/network separation | Claim scope commits/disposes before actual broker I/O; independent NOWAIT probe can lock claimed row during publish; new result scope |
| Conditional completion | Status/owner/token/live expiry predicate; old publisher cannot acknowledge a newer/expired claim; Published/envelope SQL facts cannot regress |
| Durable broker handoff | Durable topic exchanges, quorum queues and bindings declared before persistent mandatory publish; 7.2.2 tracked confirms reject returns/NACKs; Published only after awaited success |
| Bounded failure recovery | Eight claims maximum; scheduled 5/15/30/60/120/300/600/600-second backoff; permanent configuration/payload failures and exhaustion retain Failed; unknown failures/shutdown leave recoverable leases |
| Uncertainty/duplicates | Real broker ACK dropped by frame-aware proxy -> actual timeout -> same-ID/body duplicate after retry; broker success followed by failed local SQL ACK also recovers by lease |
| Async/channel ownership | One gated channel with no concurrent frame publishing, fresh reconnect/redeclare after classified failures; 12 concurrent publisher calls verified; cancellation propagates |
| Finite retry/dead letter | Four durable queues, one delayed retry-work stage then parked dead queue; delivery limit five, max length/reject-publish, at-least-once quorum dead lettering; actual rejection/TTL/parking test has no requeue cycle |
| Restart/upgrades | Persistent queued event survives broker restart; populated previous-schema outbox survives additive migration/reapplication and publishes original identity |
| Development/operations | Private secret-file wrapper, loopback ports, stable-node named volume/health check; opt-in Worker only; [runbook](messaging.md) covers unavailable/routing/NACK/timeout/config/Failed/DLQ recovery without invented replacement IDs |

### Luồng chính (Vietnamese)

Checkout và các lệnh nghiệp vụ chỉ ghi OutboxMessages trong transaction PostgreSQL cùng order/stock/history. Broker tắt không làm checkout thất bại. Worker lấy một event đến hạn bằng SKIP LOCKED, lưu owner/token/lease 60 giây và tăng lượt, rồi commit và giải phóng context. Sau đó mới kết nối RabbitMQ, khai báo exchange/queue/binding durable, gửi message persistent + mandatory và chờ tracked confirm. Transaction không bị giữ trong lúc chờ mạng.

Khi broker xác nhận và không trả unroutable/NACK, một câu UPDATE mới chỉ chuyển Published nếu owner/token/lease vẫn hợp lệ. Lỗi transient quay về Pending với NextAttemptAt; lỗi permanent hoặc hết tám lượt giữ Failed để xem xét. Nếu ACK hoặc SQL acknowledgement bị mất, lease hết hạn cho phép gửi lại cùng EventId/body. Vì thế delivery là at least once; consumer tương lai phải dedup bằng inbox riêng. Confirm không chứng minh email/reporting/nghiệp vụ downstream đã xong.

Topology có đường hữu hạn main work -> delay TTL -> retry-work -> dead queue, không tự quay vòng. Message lỗi được giữ để điều tra; chưa có consumer nghiệp vụ hoặc công cụ audited redrive. Một semaphore chỉ bảo vệ thứ tự frame trên IChannel; PostgreSQL mới bảo vệ claim giữa nhiều process.

### Important files and migration

- Application/Messaging/EventEnvelope + OutboxMessage: transport contract, typed publish failures and producer trace snapshots.
- Application/Messaging/OutboxPublishingIteration: claim/commit, external publication, token-fenced SQL success/backoff/failure and exhausted-lease recovery.
- Worker/Messaging/RabbitMqSettings, RabbitMqTopology, RabbitMqEventPublisher, OutboxPublisherWorker; WorkerHost/appsettings: opt-in composition, bounded async loop, real client/topology and safe channel ownership.
- Migration **20261112030213_AddOutboxPublishing**, designer/snapshot and OutboxMessageConfiguration: additive nullable failure/trace fields, EF lease concurrency/immutable envelope metadata, SQL identity/Published/attempt guards and no-delete/no-truncate protection. Down refuses evidence loss. Existing rows retain original identity/data with optional trace null. No developer migration/data reset.
- compose.yaml, infra/rabbitmq/entrypoint.sh, .env.example, central package version and generated locks.
- Messaging integration collection/server, ConfirmDroppingProxy, OutboxPublishingTests, OutboxCompositionTests, EventEnvelopeTests and migration-count regression (9 -> 10).
- [Messaging runbook](messaging.md), [ADR-021](adr/0021-rabbitmq-outbox-publication.md), current architecture/spec/persistence/testing/README and this status.

### Verification evidence

All .NET commands used DOTNET_CLI_HOME=/tmp/orderflow-callbacks/dotnet-home and NUGET_PACKAGES=/tmp/orderflow-callbacks/packages. Logs/TRX/SQL are under /tmp/orderflow-outbox; commands used installed SDK 10.0.115 matching the independently updated global.json. Actual PostgreSQL/RabbitMQ tests use disposable owned infrastructure; no developer database migration or volume reset.

1. Restored the added pinned package/generated locks, then final `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-outbox/artifacts --disable-parallel -m:1` — exit 0, all six projects, auditing enabled; `/tmp/orderflow-outbox-restore-locked.log`.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-outbox/artifacts -m:1` — exit 0, **0 warnings/errors**; `/tmp/orderflow-outbox-build-final.log`.
3. `dotnet ef migrations add AddOutboxPublishing --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with matching ArtifactsPath/design-only connection — generated additive migration, then reviewed/customized immutable/retention guards; `/tmp/orderflow-outbox-migration.log`. The populated prior-schema upgrade/reapplication and original event publication pass in the full suite.
4. Focused `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-outbox/artifacts --filter 'FullyQualifiedName~OutboxPublishingTests|FullyQualifiedName~OutboxCompositionTests|FullyQualifiedName~EventEnvelopeTests' --logger 'trx;LogFilePrefix=outbox-focused-final' --results-directory /tmp/orderflow-outbox/results` initially passed 20/21 after expanding acceptance: the remaining SQL guard test used unescaped literal JSON braces in ExecuteSqlRaw and failed in EF formatting before SQL. Corrected escaping; the final full run below includes the passing test. Earlier fixture startup failed because root diagnostics created a root-owned Erlang cookie; switched test readiness to server log/TCP and Compose diagnostics to gosu rabbitmq. Earlier NACK test incorrectly assumed the second publish must exceed quorum max-length; now it observes an actual NACK before checking outbox state. Both corrected tests pass with actual broker, without sleeps/fake success. Prior diagnostic logs retained under /tmp/orderflow-outbox-*.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-outbox/artifacts -m:1 --logger 'trx;LogFilePrefix=outbox-full' --results-directory /tmp/orderflow-outbox/results` — exit 0, **361 integration + 129 unit = 490 passed**, **0 failed/skipped**. Includes **16 new real PostgreSQL/RabbitMQ cases**, one composition case and four envelope cases; baseline was 469. Integration duration 3m17s. Evidence `/tmp/orderflow-outbox-full.log`; TRX `outbox-full_net10.0_20261112101632.trx` and `outbox-full_net10.0_20261112101633.trx`. No production code changed after this full run.
6. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with ArtifactsPath=/tmp/orderflow-outbox/artifacts and nonconnecting design connection — exit 0, **no model drift**, `/tmp/orderflow-outbox-drift.log`. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-outbox/migrations.sql` — exit 0; reviewed additive trace/failure columns and identity/Published/retention SQL guards; `/tmp/orderflow-outbox-script.log`.
7. Committed Compose `config --quiet` — exit 0. Literal `up` failed because this Snap Docker daemon cannot see the session's /tmp bind-secret path (same infrastructure limitation recorded in 03). A **temporary verification-only override** removed bind mounts; `compose create`, `docker cp` of the exact committed entrypoint and temporary private secret, then `compose start --wait --wait-timeout 90 rabbitmq` — healthy. No credentials were embedded in override/output. Actual `rabbitmq-diagnostics -q ping` succeeded and private management API authentication for orderflow vhost returned **200**. Same image/hostname/ports/volume/script/health configuration exercised; literal bind mounting remains unverified in this daemon. Logs `/tmp/orderflow-outbox-compose*.log`. Cleanup `compose down` without --volumes preserves the dedicated `orderflow-outbox-check_rabbitmq_data` volume; no developer data touched.
8. `sh -n infra/rabbitmq/entrypoint.sh` and `git diff --check` — exit 0. Local Markdown targets/config parsing and final Git diff reviewed; no generated artifacts/private secrets added. No commit/push/deploy.

Unverified: production clustered broker failover, partitions, independent OS-kill/uncertain PostgreSQL commit, TLS/runtime permissions, real business consumers and audited redrive. The tested crash windows use local SQL acknowledgement failure and deterministic lease advancement; they do not claim a killed process or end-to-end exactly-once.

### Manual verification and limits

Follow [messaging setup and recovery](messaging.md): create private broker/DB secrets once, start Compose, explicitly migrate, privately configure Worker RabbitMq password and enable OutboxPublishing. Stop only the broker; perform authenticated checkout and inspect Pending. Restart broker; inspect same EventId becoming Published and the durable integration queue gaining the persistent envelope. Observe expiry/backoff and duplicate identity in the focused acceptance tests. Never manually set Published or create a new EventId to repair uncertainty.

The integration queue is deliberately undrained until business consumers ship in 23/24; it is a real durable destination, not a claim that notifications/reporting execute. Hardcoded versioned queue arguments simplify repeatable setup but incompatible changes require a forward drain/migration plan. One-node development quorum persistence has no multi-node HA guarantee. Automatic publication stops at eight claims; Failed/DLQ rows remain for review and future audited same-identity replay (28). Evidence retention prevents destructive downgrade/pruning; a reviewed future archival policy is needed. Production TLS/runtime roles, broker clustering/partition/OS-kill behavior, business consumers and end-to-end exactly-once are not claimed. No user data/volume was reset or commit/push/deploy performed.

### Three interview questions

1. **Vì sao publish ngoài transaction và chỉ ghi Published sau confirm?** Hiểu outbox giữ checkout độc lập broker và tránh mất event khi commit/network lỗi; broker confirm chỉ là handoff.
2. **Lease token có ngăn gửi trùng không?** Không; nó chặn acknowledgement cũ ở PostgreSQL. Mất confirm vẫn có thể phát trùng, cần EventId ổn định và consumer inbox/effect transaction.
3. **Mandatory khác confirm, và DLQ khác retry thế nào?** Mandatory phát hiện không có route; confirm phản ánh broker nhận/chấp nhận. Backoff outbox và finite delay/retry-work/DLQ xử lý hai giai đoạn khác nhau, không dùng vòng requeue vô hạn.

Suggested commit title (none supplied): `feat: publish durable outbox events to RabbitMQ`.


## Durable RabbitMQ order notifications — 23 (2026-11-12)

### Scope and decisions

Implements the requested local user notification consumer/read workflow on the existing outbox publisher, with no new package/SDK/lockfile change. Initial Git status was clean; instructions/spec/architecture/ADRs, current client 7.2.2, package locks, ten existing migrations and test harness were inspected. Official RabbitMQ .NET guide and 7.2.2 source verified consumer buffer lifetime, async channel options, dispatch concurrency/prefetch and manual ACK semantics. [ADR-022](adr/0022-durable-user-notifications.md) records routine contract details: seven lifecycle events, one authoritative order-owner recipient, Type as the v1 template identity, read APIs and explicit schema/failure behavior. This realizes the planned local event/template/recipient delivery as readable Notifications; no incompatible payment/order behavior is changed.

No email, socket/realtime delivery, reporting, Admin broadcast, general refund/return, automatic historical backfill, audited redrive or deployment is added. NotificationCreated is a durable future realtime intention only.

### Acceptance delivered

| Criterion | Result |
| --- | --- |
| Notification persistence | Recipient/source EventId/type/order/created/read; unique source/type/recipient, restrictive user/order FKs, list/unread indexes and SQL owner/identity/read guards |
| Atomic effects and dedup | Consumer notifications.orders.v1 + EventId unique INSERT ON CONFLICT then FOR UPDATE; type/schema/exact-body hash correlation; Notification + Processed inbox + NotificationCreated outbox commit together |
| Simultaneous duplicates | Two real subscriptions, prefetch one, distinct contexts rendezvous before contested inbox INSERT; both ACK, exactly one notice/inbox/outbox |
| Independent consumers | Same EventId with another ConsumerName remains independent; conflicting bytes under handled identity rejected, no evidence overwritten |
| Commit-before-ACK/crash | Real ACK-boundary fault proves all three effects visible before ACK; real channel close/redelivery creates no duplicate, read state also survives later duplicate |
| Relevant supported events | created/confirmed/cancelled/expired/processing/shipped/completed v1; recipient checked against stored owner and snapshot against immutable history; old events cannot change current order/stock |
| Envelope/schema safety | <=64 KiB strict UTF-8/object JSON, depth 32, duplicate-key rejection, header/body IDs/type, provenance/trace and required order snapshot validation; older/missing/newer versions are poison |
| Bounded RabbitMQ work | Independent durable notification main/delay/retry/dead topology; one delayed retry then parked, at-least-once DLX/reject-publish/delivery limit; no immediate requeue loop |
| Scoped concurrency/shutdown | One connection/channel with sequential dispatch per subscription, manual single-delivery ACK/NACK, fresh scope/DbContext per delivery; 1–4 consumers per queue, prefetch 1–32; cancellation preserves unacknowledged work |
| Failure classification | Poison and classified transient PostgreSQL failures NACK(false) through finite topology; full rollback including partially written notification; unknown exceptions fault host; broker reconnect/session attempts stop after eight per slot/run |
| Authenticated API | Own list/detail/unread/read; no client recipient ID; Admin has no override; non-owner/missing 404, real cookies/CSRF enforced, bounded pagination, no-store and concurrent idempotent read receipt |
| Migration/regression | Populated previous-schema order/event survive additive upgrade/reapplication; publisher mandatory-return/full-queue tests adapt to independent fan-out bindings; no developer DB/data reset |

### Luồng chính (Vietnamese)

Publisher vẫn gửi event đã commit vào exchange durable. Queue notification riêng chỉ bind bảy sự kiện order; consumer khác giữ queue và inbox identity riêng. Callback consumer kiểm tra bytes/schema/MessageId/Type rồi tạo scope PostgreSQL mới. Trong transaction, INSERT inbox với ON CONFLICT và khóa dòng thắng để xử lý duplicate đồng thời. Dữ liệu order trong DB quyết định người nhận; history xác nhận version/status nên event cũ vẫn xử lý được mà không sửa trạng thái order.

Notification, inbox Processed và NotificationCreated outbox được lưu trong cùng transaction. Chỉ sau commit và giải phóng scope mới ACK một delivery. Nếu crash/mất ACK sau commit, lần nhận lại thấy marker hợp lệ và không tạo thêm effect. Cùng EventId nhưng body khác bị giữ theo poison path. Không gửi email hoặc gọi broker trong transaction DB.

Lỗi JSON/schema/ownership hoặc lỗi DB transient NACK với requeue=false: main -> delay -> retry-work -> dead queue, không quay vòng vô hạn. Mỗi subscription xử lý tuần tự trên channel riêng, tạo DbContext riêng cho từng message. API lấy user từ cookie, chỉ list/count/get/read dòng của user đó; read là UPDATE có điều kiện ReadAt IS NULL, nên hai request đồng thời vẫn giữ timestamp đọc đầu tiên. Replay event không làm unread trở lại.

### Important files and migration

- Application/Modules/Notifications/Notification, OrderNotificationEvent, NotificationProcessor, NotificationService: data/contract validation, consumer transaction and own read operations/DTOs.
- Api/Notifications/NotificationsController + Program: thin authenticated list/detail/unread/read routes; existing global browser CSRF retained.
- Worker/Messaging/NotificationConsumerOptions, NotificationConsumerSubscription, NotificationConsumerWorker, NotificationDeliveryHandler; WorkerHost/appsettings: opt-in independent subscriptions, scopes/manual ACK, safe failure propagation/load bounds.
- RabbitMqTopology/RabbitMqSettings: additive independent notification quorum queues/exchanges/exact bindings; old integration destinations preserved. Publisher declares new queues even when consumer disabled; NotificationCreated has no recursive binding.
- Migration **20261112035709_AddNotifications**, designer/snapshot/NotificationConfiguration and DbContext: additive table/indexes/checks/FKs, immutable notification/read facts, authoritative owner guard and handled notification inbox update/delete guard. Down refuses evidence loss. Migration count 10 -> 11; no developer DB applied/reset.
- NotificationTests/NotificationHttpTests/NotificationCompositionTests/NotificationTestSupport and OrderNotificationEventTests; two publisher topology regressions adjusted for real fan-out.
- [Notification runbook](notifications.md), [ADR-022](adr/0022-durable-user-notifications.md), spec/architecture/persistence/testing/README/.env.example and this status.

### Exact checks and evidence

All .NET commands used DOTNET_CLI_HOME=/tmp/orderflow-callbacks/dotnet-home and NUGET_PACKAGES=/tmp/orderflow-callbacks/packages, with installed/pinned SDK 10.0.115. Artifacts/logs/TRX/SQL are outside the repository under /tmp/orderflow-notifications. No SDK/package/lockfile changes, fake broker/database, success skips, developer DB migration or user volume reset.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-notifications/artifacts --disable-parallel -m:1` — exit 0/all six projects with auditing enabled; `/tmp/orderflow-notifications-restore.log`.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-notifications/artifacts -m:1` — final exit 0, **0 warnings/errors**; `/tmp/orderflow-notifications-build-final.log`. Initial compilation caught the 7.2.2 CreateChannelOptions constructor requiring explicit confirmation booleans and a missing fixture namespace import; corrected before acceptance runs.
3. `dotnet ef migrations add AddNotifications --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with ArtifactsPath=/tmp/orderflow-notifications/artifacts and nonconnecting design connection — exit 0; `/tmp/orderflow-notifications-migration.log`. Reviewed generated migration and added recipient/immutable/read/handled-inbox guards and forward-only evidence retention. Actual Up/populated upgrade/reapplication ran only in owned test databases.
4. Focused `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-notifications/artifacts -m:1 --filter 'FullyQualifiedName~Notification|FullyQualifiedName~OutboxPublishingTests' --logger 'trx;LogFilePrefix=notifications-focused' --results-directory /tmp/orderflow-notifications/results` initially passed 32/33 integration + 21 unit: a test mutated CorrelationId to check collision and then incorrectly reused the mutated value in its unchanged-evidence assertion. Restored the original expected value. Expanded `... --logger 'trx;LogFilePrefix=notifications-focused-final' ...` passed 34/35 integration + 21 unit; only legacy fixture failed because the real development seeder correctly refuses pending migrations. Replaced old-schema setup with direct unchanged domain fixtures (as existing migration tests do), retaining the production seeder guard. Logs `/tmp/orderflow-notifications-focused.log` and `...-focused-final.log`.
5. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-notifications/artifacts -m:1 --filter 'FullyQualifiedName~Populated_previous_schema_upgrades_without_losing_original_event_or_order' --logger 'trx;LogFilePrefix=notifications-upgrade' --results-directory /tmp/orderflow-notifications/results` — exit 0, **1 actual PostgreSQL upgrade case passed**; UnitTests has no matching case (not a skip/fallback); `/tmp/orderflow-notifications-upgrade.log`.
6. Full `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-notifications/artifacts -m:1 --logger 'trx;LogFilePrefix=notifications-full' --results-directory /tmp/orderflow-notifications/results` — exit 0, **380 integration + 150 unit = 530 passed**, **0 failed/skipped**, integration duration **3m41s**. Baseline 490; adds **12 real PostgreSQL/RabbitMQ cases**, **6 PostgreSQL/HTTP cases**, **1 composition case** and **21 parser cases**. Includes all corrected tests and existing payment/stock/security/publisher regressions. Evidence `/tmp/orderflow-notifications-full.log`; TRX `notifications-full_net10.0_20261112150702.trx` and `notifications-full_net10.0_20261112150703.trx`. No production/test code changed after this run.
7. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with matching ArtifactsPath/design connection — exit 0, **no model drift**; `/tmp/orderflow-notifications-drift.log`. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-notifications/migrations.sql` — exit 0; reviewed additive DDL, unique/FK/indexes and notification/handled-inbox SQL guards; `/tmp/orderflow-notifications-script.log`.
8. `git diff --check` — exit 0. Local documentation/config checker validated **42 Markdown files / 406 local targets**, balanced fences and Worker JSON. Git diff reviewed; no credentials/generated artifacts included. Existing source package pins/lockfiles/global.json/Compose preserved. No user data deletion or commit/push/deploy.

Unverified: independently killed OS processes, clustered broker failover/network partitions/uncertain PostgreSQL commit, real email/realtime transport, audited redrive/historical backfill and production TLS/runtime roles/HA. The tested crash is an injected post-commit/pre-ACK failure plus actual broker channel closure/redelivery, not an OS-kill claim. Finite malformed/transient failure paths were observed through real broker subscriptions and DLQ callbacks.

### Manual verification / trade-offs

Follow [notification runbook](notifications.md): private DB/broker setup -> explicit migration -> API + Worker with OutboxPublishing and Notifications enabled -> authenticated checkout/cancel -> own list/unread -> CSRF-protected /read -> repeat timestamp -> second user/Admin denied another user's detail/read. Run focused tests for deterministic redelivery/failure windows. Broker inspection uses read-only queue metadata; never consume/discard DLQ for diagnosis.

Notifications represent immutable event notices, not latest-state projections. One owner receives one notice/type/event; historical arrival is allowed and CreatedAt is local processing time. No automatic backfill of already Published events predating the new binding. Finite message retry intentionally parks prolonged outages for review; bounded reconnect failure stops Worker for operator restart. Exact-body hash requires preserving original raw envelope on redrive. Existing integration queue remains undrained by this consumer; independent consumers need separate bindings/inbox identities. Production TLS/roles/cluster HA, OS-process kill/network partition/uncertain SQL commit, email/realtime/UI, archival and audited replay remain future work. No exactly-once transport/external-effect claim or user data deletion.

### Three interview questions

1. **Vì sao unique inbox INSERT và notification phải chung transaction?** Hiểu concurrent duplicate phải tranh ở PostgreSQL; rollback phải bỏ cả marker/effects; check-exists riêng không đủ.
2. **Crash sau commit nhưng trước ACK có làm trùng không?** Broker có thể giao lại; consumer identity + EventId và unique effect làm lần sau no-op, không reset ReadAt hoặc tạo thêm NotificationCreated.
3. **Vì sao recipient/read không lấy từ request hoặc chỉ payload?** Cookie xác định người đọc; order owner/history trong DB xác nhận người nhận và event. Admin cũng không được đọc chéo qua endpoint này; CSRF vẫn cần cho POST.

Suggested commit title (none supplied): `feat: add durable order notifications and owner-scoped APIs`.


## Durable order-confirmation email — 23 email slice (2026-11-12)

### Scope and decision

The explicit request extends ADR-022/spec v1's former local-notification-only scope. This was reported before dependent behavior and recorded in [ADR-023](adr/0023-order-confirmation-email-delivery.md). Reuses the existing order-event consumer/inbox; adds confirmation intention and separate gateway sending, not a second generic bus/consumer framework. No production customer email, campaign/welcome/cancellation email, real payment provider, general refunds, realtime/UI, automatic historical backfill or general outbox replay is introduced.

Repository instructions/spec/architecture/ADRs, clean initial Git status, current code/tests/locks and eleven existing migrations were inspected. SDK/package/lockfile pins remain unchanged. The .NET 10 cancellable SendMailAsync API and Mailpit official release/Docker/options/health contract were checked. Mailpit v1.33.1 was pulled and digest pinned; BCL SMTP is restricted to a Development/Testing loopback sink and `.invalid` recipients. No SMTP credentials or real customer email were added.

### Acceptance delivered

| Criterion | Behavior/evidence |
| --- | --- |
| Atomic intention/dedup | Existing confirmed-event transaction writes EmailDeliveries + handled consumer inbox + notification/NotificationCreated; concurrent same EventId and different source identities for same order/template retain one intention |
| Stable snapshots | Source/order/owner, recipient/from/template/subject/plain-text/server USD total, MIME Message-ID, provider/key/request hash are immutable; changing account email cannot change the queued request |
| Safe recipient | Stored owner/history correlation retained; invalid/missing email goes to review rather than failing event processing; local gateway rejects non-.invalid real addresses before network |
| Claim/network/result boundary | One SKIP LOCKED row per short transaction, owner/token/60-second lease and conservative uncertainty, scope disposed before lookup/send; independent NOWAIT probes pass at both operations |
| Durable bounded schedule | Pending/InFlight/Sent/NeedsReview, lifetime/cycle counters, due/failure/sent metadata, eight attempts/cycle/backoff, bounded exhausted-lease cleanup, 20/pass and cancellable polling |
| Fenced completion | Conditional status/owner/token/unexpired lease update; expired owner cannot overwrite newer completion; lost local ACK leaves claim for recovery |
| Capable provider recovery | Same key/body/digest for lookup/idempotent send; pending/rejected/correlation results respected, lost acceptance/local result recovered with one effective send in controlled provider |
| SMTP uncertainty | Actual Mailpit acceptance followed by failed local Sent acknowledgement yields two accepted messages after same-identity lease recovery; stable Message-ID does not imply exactly-once |
| Shutdown/concurrency | Separate scoped contexts, deterministic two-worker claim gate, accepted-provider TCS/cancellation leaves uncertainty and lease; no context sharing or immediate retry loop |
| Admin metadata/retry | Server Admin policy/cookie/CSRF; metadata excludes address/subject/body/hash, retry locks delivery then audits operation/actor/reason/cycle/risk; replay idempotent, changed facts conflict, Sent/Pending/InFlight reject new retry |
| Audited bounded cycle | Retry preserves ID/key/snapshots/lifetime count, resets only cycle attempts with matching immutable SQL audit; uncertain work requires risk acknowledgement; terminal Sent/identity/evidence cannot regress |
| Mail outage demo | Actual sink stopped before checkout/payment, order 201/payment 202/confirmed capture+consumption continue, email Pending; restart/due time sends same intention to local sink |
| Development Compose | Pinned Mailpit, loopback 1025/8025, persistent retained volume, quiet/version-check disabled/no relay; literal dedicated Compose profile healthy without bind-mount workaround |

### Luồng chính (Vietnamese)

Consumer order-confirmed lấy owner email và tổng tiền từ DB, chụp recipient/template/body và tạo EmailDelivery với ID/provider key/Message-ID cố định. INSERT ON CONFLICT theo order/template và unique constraints bảo vệ duplicate; intention commit cùng inbox, notification và outbox trước RabbitMQ ACK. Checkout/settlement không gọi SMTP. Event đã Processed từ trước milestone không tự tạo email ngược lại; không xóa/reset marker để backfill.

Sender claim một email đến hạn bằng SKIP LOCKED, lưu owner/token/lease và tăng counters/uncertainty rồi commit, giải phóng scope. Ngoài transaction mới lookup/send qua gateway. Provider hỗ trợ idempotency/lookup dùng cùng key/hash; kết quả Pending không bị gửi mới mù quáng. SMTP không có hai capability này nên sau crash có thể gửi trùng. Transaction/UPDATE mới chỉ ghi kết quả nếu lease vẫn thuộc worker. Lỗi transient backoff, permanent/exhausted chuyển NeedsReview; shutdown/unknown error giữ claim có thể phục hồi.

Admin đọc metadata an toàn rồi retry sau khi kiểm tra nguyên nhân. Command có operationId và audit, cần CSRF và acknowledgement khi có duplicate risk. Nó không tạo EmailDelivery/key mới hay sửa snapshot; chỉ mở một chu kỳ tối đa tám lượt. Replay cùng operation không reschedule; Sent không cho phép gửi lại bằng operation mới. Sent chỉ nghĩa provider/sink đã nhận handoff, không chứng minh người dùng nhận/đọc thư.

### Important files and migration

- Modules/Emails/EmailDelivery, IEmailGateway, EmailSendingIteration, EmailAdministration: snapshots/request hash/capability contract, three-phase claim/network/fenced result and safe operator metadata/retry.
- Existing NotificationProcessor now creates only confirmation email intentions in its atomic transaction; other order/payment use cases are unchanged.
- Worker/Emails/DevelopmentSmtpEmailGateway, EmailSendingWorker; WorkerHost/appsettings: opt-in Development/Testing/local-only SMTP, bounded asynchronous budgets/batches and independent scopes.
- Api/Emails/AdminEmailDeliveriesController + Program: Admin-only no-store metadata and CSRF retry API, no sensitive message DTO/mark-Sent endpoint.
- Migration **20261112082134_AddEmailDeliveries**, designer/snapshot, EmailDeliveryConfiguration and DbContext: additive EmailDeliveries/EmailDeliveryRetries, unique/FK/time/state/counter/lease checks, immutable request/Sent/audit guards and audited cycle resets. Down refuses evidence loss. Migration count 11 -> 12; no developer DB applied/reset.
- Compose Mailpit + .env.example; dedicated Email test collection/Mailpit fixture, controlled provider, EmailDelivery/Admin/Composition/ServiceOutage tests and snapshot/recipient unit tests.
- [Email runbook/demo](email-delivery.md), [ADR-023](adr/0023-order-confirmation-email-delivery.md), current spec/architecture/persistence/testing/notification/README and this status.

### Exact checks and evidence

All .NET commands use DOTNET_CLI_HOME=/tmp/orderflow-callbacks/dotnet-home and NUGET_PACKAGES=/tmp/orderflow-callbacks/packages, matching pinned SDK 10.0.115. Logs/TRX/SQL/artifacts are outside the repository under /tmp/orderflow-email. No package/lockfile/SDK changes, production customer messages/funds, fake PostgreSQL fallback or developer DB migration/reset.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-email/artifacts --disable-parallel -m:1` — exit 0/all six projects with auditing; `/tmp/orderflow-email-restore.log`.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-email/artifacts -m:1` — final exit 0, **0 warnings/errors**; `/tmp/orderflow-email-build-final.log`.
3. `dotnet ef migrations add AddEmailDeliveries --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output-dir Persistence/Migrations` with matching ArtifactsPath/design-only connection — exit 0; `/tmp/orderflow-email-migration.log`. Reviewed generated additive DDL and added immutable request/Sent/owner/history/audit/cycle guards and forward-only retention. Actual Up is exercised by owned test databases; no merchant developer database updated.
4. Initial focused `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-email/artifacts -m:1 --filter 'FullyQualifiedName~Email' --logger 'trx;LogFilePrefix=email-focused' --results-directory /tmp/orderflow-email/results` passed 17/18 integration + five unit; outage fixture retained the pre-restart randomly mapped Docker SMTP port. Production Compose uses fixed host port; fixture refreshes endpoint after restart. `... --filter 'FullyQualifiedName~EmailServiceOutageTests' --logger 'trx;LogFilePrefix=email-outage' ...` then passed its **one real outage/payment case**; UnitTests has no matching case, not a fallback/skip. Logs `/tmp/orderflow-email-focused.log` and `...-outage.log`.
5. Expanded focused run `... --filter 'FullyQualifiedName~Email' --logger 'trx;LogFilePrefix=email-expanded' ...` passed 20/21 integration + five unit; the duplicate test was reading Mailpit search `total` (entire sink) instead of filtered `messages_count`. Verified the actual API using two synthetic `.invalid` messages in dedicated Compose: total=2, filtered count=1; corrected fixture count. No application behavior/failure was hidden. Final full focused command with `--logger 'trx;LogFilePrefix=email-focused-final'` — exit 0, **21 integration + five unit passed**, no failed/skipped; `/tmp/orderflow-email-focused-final.log` and TRX `email-focused-final_net10.0_20261112154355.trx` / `...154356.trx`.
6. Full `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-email/artifacts -m:1 --logger 'trx;LogFilePrefix=email-full' --results-directory /tmp/orderflow-email/results` — exit 0, **401 integration + 155 unit = 556 passed**, **0 failed/skipped**, integration duration **4m2s**. Baseline 530; adds **19 PostgreSQL/Mailpit cases**, **one PostgreSQL/Admin HTTP case**, **one composition case** and **five snapshot/recipient unit cases**. Includes all original payment/order/stock/cookie/CSRF/broker/notification regressions and the actual mail-off demo. Evidence `/tmp/orderflow-email-full.log`; TRX `email-full_net10.0_20261112155128.trx` and `email-full_net10.0_20261112155129.trx`. No production/test code changed after this run.
7. `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with ArtifactsPath=/tmp/orderflow-email/artifacts and nonconnecting design connection — exit 0, **no model drift**, `/tmp/orderflow-email-drift.log`. `dotnet ef migrations script --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-email/migrations.sql` — exit 0; reviewed additive table/unique/constraint/audit retention SQL; `/tmp/orderflow-email-script.log`.
8. Pulled official `axllent/mailpit:v1.33.1` and pinned digest sha256:aef2aacc11f075ec0fb6874275ee4a421e688e11a96ebb296134d1735c7761ce; `/tmp/orderflow-email-image.log`. Literal committed Compose profile `config --quiet` and dedicated project `up -d --wait --wait-timeout 60 mailpit` — exit 0/**healthy**, no daemon bind workaround needed. Verification used loopback ports 11027/18027; no relay/auth secrets. Only synthetic `.invalid` probe messages were sent. `down` without --volumes removed only verification container/network; inspected retained `orderflow-email-check_mailpit_data` volume. Logs `/tmp/orderflow-email-compose.log` and `...-compose-down.log`.
9. `git diff --check` — exit 0. Documentation/Worker JSON checker validated local targets/fences/config; final Git diff reviewed, no generated artifacts/private credentials included. No commit/push/deploy.

Unverified: real production email provider/recipient delivery/read, durable provider store across provider OS restarts, independent OS-kill/network partition/uncertain SQL commit, TLS/runtime roles/cluster failover, automatic historical backfill/snapshot repair and general replay. SMTP duplicate acceptance and stopped-service recovery are real sink tests; provider one-effective-send is conditional on the controlled fake's key/store contract within test lifetime, not universal exactly-once delivery.

### Manual demo / operational limits

Follow [email guide](email-delivery.md): explicitly migrate, start Mailpit, privately configure DB/RabbitMQ/payment simulator, enable Notifications/OutboxPublishing/EmailSending plus payment workers, then stop only mailpit. Authenticated checkout/payment continue and order confirms; inspect Pending email/backoff. Start mailpit, await due time, inspect same ID/key Sent and synthetic message. Executable demo command: `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~EmailServiceOutageTests'`.

SMTP can duplicate accepted mail after a crash/timeout/lost local acknowledgement; actual duplicate sink messages were tested. One-effective-send claims apply only to the controllable provider's advertised idempotency/lookup contract within its test-store lifetime, not production/provider restarts. A production provider must durably bind key/request/result through the full recovery window. Lookup-only Absent after uncertainty stays review; no unsafe inference cancels a stale in-flight call.

Existing already-Processed confirmations are not backfilled; blank/invalid immutable recipient may need a reviewed future repair rather than blind retry. No production mail adapter, full customer-email flow, recipient read/delivery tracking, mail authentication/TLS, general replay/UI, archival, independent OS-kill/network-partition/uncertain PostgreSQL commit or cluster HA is claimed. No real customer messages/funds, user data deletion, developer volume reset or commit/push/deploy.

### Three interview questions

1. **Vì sao email intention commit cùng inbox nhưng gửi riêng?** Hiểu duplicate/crash không mất intention, SMTP không giữ business locks và mail outage không làm checkout/settlement thất bại.
2. **Stable key/lease có bảo đảm SMTP exactly once không?** Không: lease chặn local stale ACK, stable key cần provider hỗ trợ mới dedup; Message-ID tự nó không ngăn thư trùng.
3. **Vì sao Admin retry không tạo key mới hay sửa recipient?** Hiểu giữ cùng external operation để reconcile uncertainty, immutable snapshot, audit/risk acknowledgement và bounded new cycle; Sent không được resend.

Suggested commit title (none supplied): `feat: add durable order-confirmation email delivery and recovery`.


## Authenticated SignalR notification hints — 23 realtime backend (2026-11-13)

### Scope and decisions

Implements the latest request using existing NotificationCreated outbox events and durable Notifications/HTTP source of truth. No frontend is added; its integration remains a later milestone-23 slice per the request. Proxy configuration/affinity/shared protected cookie keys/forwarded-header deployment belongs explicitly to 26; no proxy config is implemented now. [ADR-024](adr/0024-signalr-notification-hints.md) records this extension and clarifies hints versus consumer/business completion.

Inspected instructions/spec/architecture/status/ADRs, current code/tests, twelve existing migrations, package locks/SDK and clean initial Git status. Official .NET 10 SignalR Redis/auth/security/scale docs and implementation source verified supported async/backplane APIs. Official NuGet flagged Redis backplane 10.0.14 under Microsoft advisory GHSA-wr8v-9x5q-ggcm; new backplane/test client pin **10.0.15**, matching installed runtime, without suppressing auditing. SDK 10.0.115, existing EF/ASP.NET pins otherwise retained. Redis **8.10.3-alpine** pulled and digest pinned.

### Acceptance delivered

| Criterion | Behavior/evidence |
| --- | --- |
| Authenticated hub | /api/hubs/notifications requires Identity cookie on negotiate/connect, strict/canonical nonempty GUID user identifier; anonymous/ambiguous/missing identity rejected |
| User isolation | No Subscribe/JoinOrderGroup/business methods; client query IDs do not change identity; Clients.User target derives from durable owner, not a selected group |
| Browser security | HTTPS/dev-only opt-in, same-origin HTTP/WebSocket guard, explicit unsafe negotiation CSRF, no access_token query auth, existing bounded request policy; WebSockets-only negotiation supported |
| Payload minimization | NotificationAvailable includes only EventId/NotificationId/OrderId; no address/body/amount/status/recipient/trace/full event. ConnectionReady has no payload |
| Durable source of truth | Existing notification inbox/outbox transaction unchanged; realtime validates notification association/schema then disposes DB scope before dispatch; no realtime handled marker or business writes |
| API-hosted delivery | Separate durable realtime RabbitMQ work/delay/retry/dead destination; API consumer only, Worker never hosts/addressed an in-memory hub |
| Cross-instance backplane | Official AddStackExchangeRedis and same environment prefix; live API A consumes committed outbox while API B owns authenticated WebSockets; DI/hub managers are independent |
| Duplicate tolerance | Same event republished delivers identical stable hint IDs with unchanged notification/inbox/read counts; dispatch success is not client receipt |
| Dependency failure | Real Redis stop produces safe logging and finite broker retry/DLQ while notification HTTP remains readable; no offline Redis hint buffering/persistence claim |
| Bounded lifecycle | Per-API main/retry subscriptions sequential per channel, prefetch 1–16, fresh scopes, five-second dispatch budget, bounded broker reconnect, expiry/server-shutdown closure |
| Reconnect catch-up | Re-authenticate/reconnect, await ConnectionReady then refetch durable HTTP; offline hints not replayed. Actual auth-expiry timer closes existing socket |
| Development/deployment contract | Digest-pinned loopback ephemeral Redis Compose healthy, no migration; documented affinity even with Redis, Upgrade/Origin/timeouts/trusted proxy/shared keys required in 26 |

### Luồng chính (Vietnamese)

Notification worker vẫn tạo Notification và NotificationCreated trong transaction PostgreSQL cùng inbox/outbox. Publisher gửi event đã commit vào RabbitMQ. Consumer chạy trong API lấy event từ queue realtime riêng, kiểm tra schema/identity và so với Notification trong DB để xác định đúng recipient. Scope DB kết thúc trước khi gọi IHubContext.Clients.User.

Mỗi API bật official Redis backplane với cùng prefix. Vì vậy API A consume vẫn gửi được tới connection của user đang ở B; Worker không có hub trong bộ nhớ để gửi xuyên process. Client không chọn user ID hoặc join order group. Hint chỉ có ba ID ổn định; client cần đọc lại HTTP notification/order theo cookie của mình. Duplicate hint không tạo thêm dữ liệu durable.

Redis/publish lỗi chỉ ảnh hưởng hint: retry qua một delay stage rồi DLQ, không sửa Notification/order. ACK sau dispatch attempt không chứng minh client đã nhận hay xử lý. Kết nối hết hạn cookie hoặc API shutdown sẽ đóng; reconnect lấy ConnectionReady rồi refetch. Redis outage cũng có thể làm mất hint trong lúc socket vẫn mở, nên frontend sau này cần refresh/focus/poll thích hợp. Proxy cần sticky session cho negotiation/WS, sẽ thực hiện trong 26.

### Important files / schema

- Api/Realtime/NotificationHub, NotificationUserIdProvider, NotificationHint: authenticated server identity, no client group API, minimal contract and readiness/expiry/shutdown semantics.
- RealtimeSettings/RealtimeRegistration/RealtimeRequestGuard: opt-in Redis setup/private safe configuration, cookie/HTTPS/CSRF/Origin/transport/rate/size bounds and redacted dependency logging.
- NotificationRealtimeDispatcher/RealtimeDeliveryHandler/RealtimeConsumerWorker: DB correlation, disposed scope before IHubContext, safe per-user hints, finite broker failure/poison path and bounded lifecycle.
- Application/Messaging/RabbitMqSettings/RabbitMqTopology moved from Worker and RabbitMqSubscription extracted from existing subscription; Worker retains thin facade. No API->Worker project reference or new generic event-bus framework. Shared RabbitMQ.Client dependency reflected in all generated locks.
- Api.Program exposes reusable Build with explicit application assembly for real Kestrel fixture; normal Main still runs the same API. Disabled realtime registers no broker/hub jobs.
- Central official SignalR Redis/client 10.0.15 pins, API/test references, generated lockfiles, API appsettings/.env.example and Redis Compose profile.
- RealtimeApiHost/RedisServer/HintConnection/RealtimeTests/RealtimeIdentityTests: actual Kestrel/WebSockets/PostgreSQL/RabbitMQ/Redis tests and service-free identity/config cases.
- [Runbook/two-API demo](realtime.md), [ADR-024](adr/0024-signalr-notification-hints.md), spec/architecture/notification/messaging/testing/README and this status.
- **No database migration/model change**; twelve existing migrations retained, durable source event and notification schema unchanged.

### Exact checks and evidence

All .NET commands use DOTNET_CLI_HOME=/tmp/orderflow-callbacks/dotnet-home and NUGET_PACKAGES=/tmp/orderflow-callbacks/packages. SDK/runtime are 10.0.115/10.0.15. Logs/TRX/artifacts stay outside the repository under /tmp/orderflow-realtime. No private Redis/RabbitMQ credentials/generated outputs were added, no developer database migration/reset and no commit/push/deploy.

1. Added official SignalR 10.0.15 package pins/references/shared RabbitMQ infrastructure and generated locks with `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-realtime/artifacts --disable-parallel -m:1` — exit 0, all six projects; `/tmp/orderflow-realtime-restore.log`. Final `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-realtime/artifacts --disable-parallel -m:1` — exit 0/all six projects, auditing enabled/no suppression; `/tmp/orderflow-realtime-restore-locked.log`.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-realtime/artifacts -m:1` — final exit 0, **0 warnings/errors**; `/tmp/orderflow-realtime-build-final.log`. Initial test-support build issues (logger class/member naming, namespace imports and async disposal/nullability in connection feature fixture) were corrected before acceptance. Early --no-build attempts against stale test artifacts are not counted as successful verification.
3. Initial live fixture run failed seven cases with 404 controller discovery: direct builder invocation from the test assembly used the wrong application assembly. API Build now explicitly names its own assembly (normal Main still calls it), enabling real Kestrel fixture controller discovery. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-realtime/artifacts -m:1 --filter 'FullyQualifiedName~Realtime' --logger 'trx;LogFilePrefix=realtime-compiled' --results-directory /tmp/orderflow-realtime/results` after successful complete build — exit 0, **14 passed**, no failed/skipped; `/tmp/orderflow-realtime-compiled.log`. UnitTests project has no matching cases, not a success skip/fallback.
4. Added explicit transport-negotiation/shutdown and wrong-kind JSON cases, then `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-realtime/artifacts -m:1 --filter 'FullyQualifiedName~Realtime|FullyQualifiedName~OutboxPublishingTests|FullyQualifiedName~NotificationTests' --logger 'trx;LogFilePrefix=realtime-expanded' --results-directory /tmp/orderflow-realtime/results` — exit 0, **44 integration passed**, no failed/skipped, 1m8s; `/tmp/orderflow-realtime-expanded.log`, TRX `realtime-expanded_net10.0_20261113090319.trx`. Includes **16 new realtime cases** and existing publisher/notification regressions after shared transport extraction.
5. Full `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-realtime/artifacts -m:1 --logger 'trx;LogFilePrefix=realtime-full' --results-directory /tmp/orderflow-realtime/results` — exit 0, **417 integration + 155 unit = 572 passed**, **0 failed/skipped**, integration duration **4m28s**. Baseline 556; adds **nine actual Kestrel/WebSocket/PostgreSQL/RabbitMQ/Redis cases** and **seven service-free strict-identity/config cases** in IntegrationTests. Evidence `/tmp/orderflow-realtime-full.log`; TRX `realtime-full_net10.0_20261113090851.trx` and `realtime-full_net10.0_20261113090852.trx`. No production/test code changed after this full run.
6. EF CLI was initially unavailable in this session; `dotnet tool restore` restored repository-pinned dotnet-ef 10.0.14 successfully (`/tmp/orderflow-realtime-tool-restore.log`). `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` with ArtifactsPath=/tmp/orderflow-realtime/artifacts and nonconnecting design connection — exit 0, **no model drift**; `/tmp/orderflow-realtime-drift-final.log`. No new migration/schema change or idempotent migration script needed for this milestone.
7. Pulled official Redis 8.10.3-alpine and pinned digest sha256:7958cfbb9d152f87dadfbc89f1b0b15938f0b81ff8982feb327476f4911935c3; `/tmp/orderflow-realtime-redis-image.log`. Committed Compose profile `config --quiet` and dedicated `up -d --wait --wait-timeout 60 redis` — exit 0/**healthy** on loopback 16387. No bind-mount workaround or credentials in config/output. `down` removed only the dedicated container/network; Redis intentionally has no persistent volume. Logs `/tmp/orderflow-realtime-compose.log` and `...-compose-down.log`; developer services/data untouched.
8. `git diff --check` — exit 0. Final documentation/API JSON/link/fence check passed, Git diff reviewed; moved transport source is retained in Application/Messaging, not discarded. No commit/push/deploy.

Unverified/deferred: production proxy/affinity/shared protected DP keys (26), frontend catch-up/logout integration (later 23 slice), continuous connection revocation, production Redis auth/TLS/runtime roles/cluster/rolling deployment and independent OS-process kill/network partitions. Actual two-API Kestrel/backplane routing, Redis outage/finite DLQ/HTTP fallback, expiration and API shutdown are tested; no durable/exactly-once client delivery or production readiness claim.

### Manual verification / trade-offs

[Realtime runbook](realtime.md) supplies Redis setup, private API/worker configuration, two separate API process commands (A consumer true/B false), native official client test/demo and HTTP catch-up flow. Executable network demo: `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Two_API_instances_Rabbit_on_A_reaches_owner_connections_on_B'`. It opens actual B WebSockets and observes A logs/hints over real Redis. The automated demo is two independent Kestrel hosts inside one test process, not an OS-process-kill claim; separate-process commands are provided for manual reproduction.

Durable HTTP remains authoritative. Redis has no offline hint history; duplicates/loss are permitted and no business-consumer/client exactly-once acknowledgement exists. Existing sockets may remain open across Redis outages, so frontend catch-up cannot rely only on socket reconnect. Cookie/security-stamp changes are not continuously revalidated on established sockets; frontend must stop on logout and reconnect with current identity. Auth expiration and server shutdown close connections.

No frontend/client app, proxy/affinity configuration, shared protected DP deployment, continuous connection-revocation service, automatic historical replay, Redis production auth/TLS/roles/cluster HA, independent OS-process/network partition/rolling deployment or production readiness claim. Direct two-port test setup avoids the future proxy; cookie-key sharing in tests is fixture-only. No user data deletion, developer DB migration/reset or commit/push/deploy.

### Three interview questions

1. **Vì sao hub/consumer phải ở API và cần Redis?** Hiểu connection thuộc process API, Worker in-memory hub không giữ connection ở process khác; official backplane chuyển message giữa các API.
2. **Hint khác notification durable thế nào?** Hint có thể mất/trùng và không chứng minh client nhận; EventId/NotificationId ổn định để dedup/refetch, HTTP/inbox/outbox mới giữ dữ liệu nghiệp vụ.
3. **Redis có bỏ được sticky session không?** Không với negotiation đang hỗ trợ; negotiate/connect vẫn cần đúng API. User identity đến từ cookie, không từ client group/query; proxy cấu hình ở 26.

Suggested commit title (none supplied): `feat: add authenticated SignalR notification hints with Redis scale-out`.


## Supplemental catalog detail caching (2026-11-13)

### Scope and decisions

Inspected repository instructions, clean Git status, catalog/HTTP/checkout/Redis composition, pinned SDK/packages/lockfiles, persistence mappings/migration chain and existing test harness. The specification originally excluded Redis/distributed cache; reported this conflict before dependent work and recorded the explicitly requested narrow exception in [ADR-025](adr/0025-bounded-catalog-cache.md), specification and architecture. Existing milestone IDs/dependencies remain unchanged; this supplement depends on catalog 07, inventory/checkout foundations and the existing Redis infrastructure. No subagents, commits, pushes, deployment, history rewrite or user-data deletion.

Select supported .NET 10 IDistributedCache cache-aside with Microsoft.Extensions.Caching.StackExchangeRedis **10.0.15** over HybridCache: no local L1 or cross-process eviction ambiguity is needed. SDK remains **10.0.115**, runtime **10.0.15**, EF/Identity pins unchanged; refresh all six affected lockfiles for necessary transitive dependencies. Cache is disabled by default and lazily connects only when enabled/used.

### Completed acceptance

| Requirement | Implemented evidence |
| --- | --- |
| Narrow public catalog scope | Only public Product/Category details; all lists and Admin reads bypass cache; no account/order/auth/checkout cache |
| Catalog-only DTO and live stock | ProductCatalogEntry contains catalog fields and CategoryId, no inventory/category snapshot; compose category independently and query Available directly every request |
| Keys/serialization/load bounds | Deployment prefix + catalog:v1 + product/category + canonical UUID N; UTF-8 camelCase JSON envelope, decimal internal price, <=16 KiB/depth 8, required fields; existing public USD strings unchanged; no negative caching |
| Explicit multi-node consistency | Distributed-only Redis; fixed absolute 30 seconds anchored before SQL, embedded deadline checked at lookup; no sliding renewal or remote L1 assumption |
| Mutation visibility/invalidation | Create/update/archive/restore/reassignment remove affected key after commit/disposal; category key avoids product fan-out; tests cover two real independent Kestrel hosts |
| Stale refill / failed removal | TCS gate observes old reader before SET, commits edit/archive + DELETE, then releases old SET; stale display allowed only until original deadline; retained-key failed DELETE test checks fresh Admin bypass and exact deadline |
| Redis outage / SQL failure | Stop actual Redis; both APIs read DB and writes commit with safe warnings; injected SQL failure remains HTTP 500; cache Get/Set/Remove failures are classified locally, cancellation/source failures propagate |
| Logs/metrics | Safe warning IDs 2501/2502, operation/kind/exception type only; bounded-tag catalog_cache_operations counter under OrderFlow.CatalogCache; metrics tested |
| Backend stock correctness | Warm catalog data survives reservation while each detail reflects live Available; checkout rejects quantity exceeding PostgreSQL availability; existing checkout/catalog races still pass |
| Repeatable measurement | Ten category-detail HTTP reads: cache disabled **10 SELECTs**, Redis enabled **1 SELECT** (1 miss, 9 hits). Two list reads still execute 4 SELECTs. No invented percentage/latency claim |

### Luồng chính (tiếng Việt)

GET detail công khai hỏi Redis bằng key cố định theo loại và UUID. Cache hit chỉ trả dữ liệu catalog còn trong hạn. Product đọc category qua key riêng rồi luôn lấy Available từ PostgreSQL; cached DTO không có tồn kho. Cache miss đọc DB bằng DbContext của request, tính hạn trước query, rồi lưu JSON nếu còn hạn và không vượt 16 KiB. Khi tắt cache, product giữ query join cũ.

Lệnh Admin vẫn kiểm tra version/quy tắc và commit DB như trước. Sau khi transaction đã dispose mới xóa key Redis; sửa category chỉ xóa category key. Redis lỗi thì ghi warning/counter và dùng kết quả DB; lỗi SQL không bị bắt thành cache miss. Checkout vẫn khóa/kiểm tra PostgreSQL và tự tính giá/tổng, không dựa vào cache.

DELETE không ngăn được một reader cũ SET sau đó. Không có L1 ở API khác; payload giữ deadline của lần đọc DB ban đầu, nên cache cũ không được chấp nhận sau 30 giây tại lookup. Crash/hủy request sau commit hoặc DELETE lỗi có cùng cửa sổ cũ; đồng hồ host cần đồng bộ. Request đã chạy có thể hoàn tất muộn hơn và product/category không phải snapshot nguyên tử.

### Important files / migration

- Application/Modules/Catalog/CatalogCache.cs: narrow DTO/cache-aside, deadline/payload bounds, failure classification and metrics.
- Application/Modules/Catalog/CatalogService.cs: cache integration, independent category/live availability, transaction-disposed mutation invalidation.
- Api/Catalog/CatalogCachingRegistration.cs, Program.cs, appsettings.json: opt-in Redis registration, safe validated prefix/config, 250ms SDK budgets and FailFast backlog.
- Directory.Packages.props, Application csproj and six package lockfiles: pinned supported provider/dependency graph.
- IntegrationTests/Catalog/CatalogCacheApiHost.cs / CatalogCacheTests.cs and UnitTests/CatalogCacheTests.cs: actual Kestrel/PostgreSQL/Redis and deterministic/failure/payload/metrics evidence.
- [Cache runbook](catalog-caching.md), [ADR-025](adr/0025-bounded-catalog-cache.md), spec/architecture/catalog/status/ADR index.
- **No new database migration/model change**, worker behavior or frontend change.

### Exact checks and results

Tooling: DOTNET_CLI_HOME=/tmp/orderflow-caching/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1; existing user NuGet package cache; all build/TRX output under /tmp/orderflow-caching. SDK/package pins above. PostgreSQL/Redis actually ran in disposable Testcontainers; no InMemory/SQLite fallback or skip. Two separate Kestrel servers/DI providers used real loopback HTTP. Generated fixture credentials stayed out of source/log output.

1. Initial sandbox restore exited 1 without diagnostics; permitted execution outside the sandbox restored all six projects. `dotnet restore OrderFlow.slnx --artifacts-path /tmp/orderflow-caching/artifacts -m:1` — **exit 0**. Final `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-caching/artifacts -m:1` — **exit 0**, all six lockfiles reproducible, no reported audit warnings.
2. `docker version` via snap could not create its runtime directory; even elevated snap launcher lacked cap_dac_override. Native `/snap/docker/current/bin/docker version --format '{{.Server.Version}}'` — **exit 0**, daemon 29.3.1. Testcontainers accessed the actual daemon successfully. No approval rejection or prerequisite blocker remained.
3. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-caching/artifacts -m:1` — **exit 0**, final build **0 warnings / 0 errors**, 5.38 seconds. Earlier implementation builds also succeeded.
4. Initial focused `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-caching/artifacts -m:1 --filter 'FullyQualifiedName~CatalogCache' --logger 'trx;LogFilePrefix=catalog-cache' --results-directory /tmp/orderflow-caching/results` — **5 integration + 9 unit passed**, 0 failed/skipped. Expanded catalog/checkout pass with the next command's filter and prefix catalog-checkout — **78 integration + 34 unit passed**, 0 failed/skipped.
5. Full regression `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-caching/artifacts -m:1 --logger 'trx;LogFilePrefix=caching-full' --results-directory /tmp/orderflow-caching/results` — **424 integration + 165 unit = 589 passed**, **0 failed/skipped**; integration duration **4m27s**. TRX `caching-full_net10.0_20261113145830.trx` / `...145831.trx`. This full run preceded the final explicit RedisTimeoutException classification and six additional timeout/server/source-failure cases.
6. Review of official installed StackExchange.Redis 2.7.27 source showed RedisTimeoutException inherits TimeoutException, not RedisException. Added its specific catch classification and GET/SET/DELETE timeout tests, while explicitly testing DB timeout propagation. After the final build, `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-caching/artifacts -m:1 --filter 'FullyQualifiedName~Catalog|FullyQualifiedName~Checkout' --logger 'trx;LogFilePrefix=catalog-final' --results-directory /tmp/orderflow-caching/results` — **78 integration + 40 unit = 118 passed**, **0 failed/skipped**; integration duration **52s**. TRX `catalog-final_net10.0_20261113150159.trx` / `...150200.trx`. This final run includes all **7 new integration + 16 new unit cases** and catalog/checkout regressions. Unrelated full suites were not repeated after this final narrow exception-classification change.
7. The measurement's captured TRX stdout reports: `GET category detail x10: cache disabled=10 SQL SELECTs; Redis enabled=1 SQL SELECTs (1 miss, 9 hits).` SQL interception counts actual EF commands, not estimated percentages. Product test asserts cold 3 vs cross-node hit 1 and live stock changes; stale stock cannot authorize another reservation.
8. `git diff --check` — **exit 0**. Markdown relative-link/fence and JSON parsing checks passed; final inspection confirms no migration/model/checkout/worker implementation changes, no real secrets or commit/push/deploy. Documentation updated after final code verification only.

Unverified limits: no independent OS-process kill, Redis cluster/partition/rolling deployment or production ACL/TLS/memory policy; no latency/throughput benchmark. UTC synchronization and in-flight response caveats apply to the 30-second lookup guarantee. SQL/source failures are not silently hidden. Full regression numbers above are deliberately distinguished from final targeted verification.

### Manual verification / remaining work

[Runbook](catalog-caching.md) contains setup, two-port API commands, Admin cookie/CSRF edit/archive/restore, Redis stop/start and executable measurement. Use the existing private DB/Redis configuration mechanism. Start the optional Redis service, enable CatalogCache__Enabled, set CatalogCache__RedisConnectionString and the same environment-specific CatalogCache__RedisPrefix on both APIs. Disabled remains the default. No developer DB was migrated/reset for verification; fixtures own generated disposable databases/keys.

Product hit still needs one live inventory SELECT. A cold product miss needs 3 queries versus 1 joined uncached query; from cold, ten product reads imply 12 total queries versus 10, even though product/category catalog projections each run once. Category reads show the measured statement reduction; no overall product latency/throughput improvement is claimed. No distributed stampede protection/durable invalidation/linearizable detail snapshot is introduced. Lists can be fresh before details. Remaining: production Redis ACL/TLS/memory/eviction policy, OS-process crash/cluster/network partition proof, latency/load benchmarking and any future managed list invalidation. Existing frontend/proxy/shared-key/reporting milestones remain unchanged.

### Three interview questions

1. **Vì sao dùng distributed-only thay vì L1 ở mỗi API?** Hiểu DELETE Redis không xóa memory của API khác; thiết kế này đổi một lượt Redis/request lấy hợp đồng nhất quán đơn giản.
2. **Vì sao TTL tính trước SELECT và có deadline trong payload?** Hiểu race SELECT cũ → mutation/DELETE → SET cũ; deadline gốc giới hạn staleness, không bảo đảm linearizable hay thời điểm giao response.
3. **Cache giảm query nào và vì sao checkout không dùng nó?** Category detail giảm 10 SELECT xuống 1 trong phép đo; product vẫn đọc inventory, miss còn tăng round trip. PostgreSQL mới quyết định giá/eligibility/reservation trong transaction.

Suggested commit title (none supplied): `feat(catalog): add bounded Redis cache-aside for public details`.


## Milestone 24 backend — direct Admin reporting and read-only audit (2026-11-13)

### Inspection, scope and decisions

Inspected AGENTS.md, clean Git status (main ahead by one existing user commit), current catalog/cache/order/history/inventory/payment/refund services and DTOs, authorization/HTTP composition, PostgreSQL harness, package pins and existing migrations/indexes/financial guards. Preserved the prior caching commit and unrelated work. No subagents, commit/push/deployment/history rewrite or developer-data reset.

The original contract/ADR-001/005/roadmap 24 required an event-fed reporting projection/checkpoint. Reported that contradiction before dependent work and recorded the latest request's direct-query preference in [ADR-026](adr/0026-direct-admin-reporting.md), spec, architecture and roadmap. This backend slice depends on existing order/payment/inventory/auth foundations, not the pending React UI. No reporting schema/table, consumer, cache, event-sourced platform or new package. SDK **10.0.115**, runtime **10.0.15**, EF/Identity/tool **10.0.14**, Npgsql **10.0.3** and all lockfiles unchanged.

### Completed acceptance

| Requirement | Implemented behavior / evidence |
| --- | --- |
| Paid sales / captured funds / refunds / net | PaidSales sums immutable line totals for allocated winning captures; CapturedRevenue sums all verified captures; Refunds only Succeeded by completion day; signed net is captured-refunded. Created/unpaid attempts do not generate revenue |
| UTC/date semantics | Required UTC dates [from,to), max 366 days, zero-filled daily results; one-microsecond-before/from/exclusive-to cases, capture-vs-created dates, DB session timezone independence and UI UTC/local guidance |
| Counts / top / low stock | Current persisted status of orders created in range; top paid quantities/line values with bounded deterministic ranking; active catalog OnHand-Reserved <= threshold, paginated/inclusive and category archival tested |
| Efficient direct reads | Parameterized PostgreSQL numeric aggregates through EF SqlQuery; database group/filter before materialization, EF projected audit/count/stock; no floating-point or whole entity graphs |
| Consistent bounded audit | Repeatable Read/READ ONLY, per-request scoped context, sequential queries/cancellation/5s lock/20s statement budgets, max 100 pagesize/checked offsets; controlled writer proves count/page snapshot consistency |
| Safe Admin audit | Nine GET endpoints total (four reports/five audit), strict existing Admin policy/no-store; history, stock ledger, attempt/capture/refund states exposed through whitelists without provider keys/raw data/leases/secrets or audit edit routes |
| Customer boundaries | Every report/audit rejects anonymous 401 and Customer 403; own-order Admin audit also 403; another owner's existing Customer order detail remains 404 |
| Measured indexes / upgrade | 100,000-row selective date/order patterns compared before/after, all five actual report SQL plans inspected; five additive indexes only, no speculative low-stock expression index; populated upgrade preserves IDs/money |
| Replay scope | No reporting projection exists; no reporting inbox/checkpoint fiction. Existing unique capture/refund identities are read once; replaying source settlement does not add report contributions |

### Luồng chính (tiếng Việt)

Admin gọi controller có policy AdminAccess. Service kiểm tra UTC from/to, paging/limit rồi mở transaction chỉ đọc Repeatable Read bằng DbContext của request. Sales tổng hợp numeric trong PostgreSQL: capture được tính riêng, winning sale lấy LineTotal bất biến, refund chỉ tính khi Succeeded. Không join nhiều item để nhân số tiền capture. .NET chỉ format decimal thành chuỗi invariant, kể cả net âm. Counts/top/low-stock có semantics riêng, không lấy Completed làm điều kiện đã thanh toán.

Audit chỉ SELECT các trường được phép, count/page cùng snapshot và trả observedAt theo clock đã inject. Đây là thời điểm server quan sát, không phải checkpoint hay mốc commit chính xác. Category/product hiện tại chỉ dùng làm nhãn/điều kiện low-stock; giá lịch sử không đọc từ catalog. Không có worker/projection/cache/report write; checkout/payment vẫn giữ luồng transaction hiện có.

Ví dụ fixture: winning sales 25.00 + 0.30 ngày 02/01 UTC; late capture của đơn Cancelled 12.50 ngày đó; refund Succeeded ngày 03/01. PaidSales=25.30, CapturedRevenue=37.80, Refunds=12.50, NetRevenue=25.30 cho cả hai ngày. Riêng ngày refund có net=-12.50 nhưng PaidSales=0.00. Pending/NeedsReview không giả thành tiền đã hoàn.

### Important files / migration

- Application/Modules/Reporting/ReportContracts.cs: bounded UTC dates, exact signed report-money formatting, explicit safe DTOs.
- Application/Modules/Reporting/ReportingService.cs: financial SQL/CTE, status/top/low-stock projections and read snapshot lifetime.
- Application/Modules/Reporting/AdminAuditService.cs: ranged/paginated whitelisted history/attempt/capture/refund/stock reads.
- Api/Reporting/AdminReportsController.cs, AdminAuditController.cs and Program.cs: strict Admin/no-store GET composition.
- Persistence Payment/Settlement/Inventory configurations; migration **20261113082834_AddReportingReadIndexes** + generated designer/model snapshot: five indexes, no business model/data rewrite.
- IntegrationTests/Reporting/ReportingScenario.cs, ReportingTests.cs, ReportingQueryPlanTests.cs; UnitTests/ReportingRulesTests.cs; existing PersistenceTests migration count/latest identity updated from 12 to 13.
- [Runbook](reporting.md), [ADR-026](adr/0026-direct-admin-reporting.md), spec/architecture/status/testing/persistence/README and ADR index. No package/lockfile changes.

### Exact execution and results

Tooling: DOTNET_CLI_HOME=/tmp/orderflow-reporting/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1, existing user NuGet cache; all binaries/TRX/SQL/plans outside the repository under /tmp/orderflow-reporting. Actual disposable PostgreSQL 18.6 via the existing Testcontainers harness; real Identity cookies/CSRF, no fake auth/InMemory/SQLite. No application package/lockfile change. Consulted official EF parameterized SqlQuery/unmapped-type docs and PostgreSQL EXPLAIN/Repeatable Read docs before version-sensitive APIs.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-reporting/artifacts -m:1` — **exit 0**, all six projects restored, no reported audit warnings. `dotnet tool restore` — **exit 0**, repository-pinned dotnet-ef 10.0.14 restored.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-reporting/artifacts -m:1` — implementation builds succeeded with **0 warnings/errors** after fixing one missing test-namespace import (initial CS0103). All final source uses supported installed APIs.
3. `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-reporting/artifacts -m:1 --filter 'FullyQualifiedName~Reporting' --logger 'trx;LogFilePrefix=reporting-initial' --results-directory /tmp/orderflow-reporting/results` — **15 integration passed**, no unit tests matched before new rule tests existed.
4. Before model index changes, focused `--filter 'FullyQualifiedName~ReportingQueryPlanTests|FullyQualifiedName~ReportingRulesTests'` / prefix reporting-plans — **1 integration + 7 unit passed**. PostgreSQL 100,000-row experiment observed one-day 274 matches (99,726 filtered), one-order 10 matches (99,990 filtered), Seq Scan before vs candidate Bitmap Index/Heap Scan after. No production improvement percentage is inferred. Eleven final JSON plans (six synthetic before/after + five actual report SQL) are under `/tmp/orderflow-reporting/plans`; actual sales/refund/top use the new date indexes and existing PKs, counts use existing Orders date index, low stock scans the small catalog/joins balances.
5. `ArtifactsPath=/tmp/orderflow-reporting/artifacts ConnectionStrings__OrderFlow='<nonconnecting design connection>' dotnet ef migrations add AddReportingReadIndexes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` — **exit 0**, generated migration 20261113082834. Same environment with `dotnet ef migrations has-pending-model-changes --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build` — **exit 0**, no model drift. The design connection contains no password and is never connected.
6. `dotnet ef migrations script 20261112082134_AddEmailDeliveries 20261113082834_AddReportingReadIndexes --idempotent --project src/OrderFlow.Application --startup-project src/OrderFlow.Api --configuration Release --no-build --output /tmp/orderflow-reporting/reporting-indexes.sql` with the same ArtifactsPath/design environment — **exit 0**. Inspected script: exactly five CREATE INDEX statements, no DROP/DELETE/UPDATE. Script was generated for review, not applied to developer data; EF upgrade itself is exercised on owned test DBs.
7. Expanded reporting run initially had **18 integration passed / 1 fixture failure**, plus **7 unit passed**: normal DevelopmentSeeder correctly refuses pending migrations. Corrected only fixture order: seed valid data on latest schema, remove this index-only migration's five indexes to reach prior schema, then migrate forward. Did not weaken the production seed/migration guard. Added empty 366-day, multi-item multiplication, timezone and snapshot regressions.
8. Final reporting command from (3), prefix **reporting-final** — **21 integration + 7 unit = 28 passed**, **0 failed/skipped**, integration **28s**. TRX `reporting-final_net10.0_20261113153843.trx` / `...153844.trx`. Includes populated migration, safe audit/authorization, source replay, deterministic date/refund/money/stock cases, actual SQL and synthetic plan checks.
9. Full `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-reporting/artifacts -m:1 --logger 'trx;LogFilePrefix=reporting-full' --results-directory /tmp/orderflow-reporting/results` initially produced **444 integration passed / 1 failed**, plus **178 unit passed**. The sole failure was the pre-existing hardcoded migration count (12 vs new 13), not a business regression. Updated that existing test to require 13 and the exact new latest migration. TRX `reporting-full_net10.0_20261113154437.trx` / `...154438.trx`.

10. Final rebuild after the migration-count test update — same command from (2), **exit 0**, **0 warnings / 0 errors**, **5.13s**. Final full suite — same command from (9), prefix **reporting-verified** — **exit 0**, **445 integration + 178 unit = 623 passed**, **0 failed/skipped**; integration duration **5m13s**. TRX `reporting-verified_net10.0_20261113155152.trx` and `reporting-verified_net10.0_20261113155153.trx` under `/tmp/orderflow-reporting/results`. No production or test code changed after this passing full run; only final documentation evidence was updated.
11. `git diff --check` — **exit 0**. Final text verification covered **28 changed/new files**, Markdown fences/relative links/whitespace/conflict markers; package/lock changes **none**. Inspected additive migration/model diff and generated SQL: exactly five read indexes, no business schema/data rewrite. No commit, push, deployment or developer DB migration/reset. Production workload/cardinality/replica/runtime-role behavior remains unverified; the documented direct-query limits and UTC semantics apply.

### Manual verification / known trade-offs

Follow [reporting runbook](reporting.md): privately configure DB, review/apply the dedicated index migration, log in as Admin and GET the nine routes with UTC date filters. Compare before/after simulated payment/refund/fulfillment/catalog price changes and reserved-stock changes; repeat as Customer to see 403. PUT/DELETE audit routes do not exist. All manual commands and the UTC versus Vietnam 07:00 display-day example are documented. No developer DB migration or user-data mutation was run here; test databases are owned/disposable.

Ordinary CREATE INDEX adds write/storage cost and can block writers during migration; it is not CONCURRENTLY. Direct reports add database work, broad ranges may scan, and individual endpoint snapshots may differ. observedAt is approximate request observation metadata, not a historical cutoff/watermark. No report UI/export, local-calendar-day ranges, historical status/stock reconstruction, production load/latency benchmark, replica/warehouse, reporting event platform or runtime-role deployment is claimed. Existing frontend/proxy/shared-key/deployment/report UI work remains pending. Aggregate decimal overflow is visible rather than rounded/truncated into false success.

### Three interview questions

1. **Completed khác Paid thế nào?** Hiểu fulfillment không tạo revenue; winning capture và immutable item prices mới xác định paid sales, còn late capture/refund là dòng tiền riêng.
2. **Vì sao net một ngày có thể âm?** Refund Succeeded có thể ở ngày sau capture; UTC [from,to) và từng timestamp source quyết định ngày, không lấy created order hay pending refund làm tiền thật.
3. **Vì sao dùng direct query + Repeatable Read thay projection?** Hiểu quy mô view nhỏ không cần consumer/inbox/checkpoint; DB group/projection/index đã đo đủ dùng. Snapshot giữ count/page nhất quán, riêng từng request vẫn có thể thấy commit khác nhau.

Suggested commit title (none supplied): `feat(reporting): add admin sales reports and read-only audit`.


## Milestone 05 + requested auth/session foundation — 2026-11-13

### Inspection, scope and decisions

Read AGENTS.md, spec/architecture/status/ADRs and actual Identity/antiforgery/ProblemDetails/order contracts; inspected clean Git status, no existing client/manifests/CI, supported Node/npm/SDK/package pins, current migrations and PostgreSQL harness before edits. Existing backend auth, rate limits, cookies, global CSRF, roles and owner constraints remain unchanged. No backend production source/migration/package/lockfile modification. No subagents, commit/push/deployment/history rewrite or developer-data reset.

Implement React/Vite milestone 05 plus the requested session/CSRF part of 20; pull only a read-only Customer orders page from 21 to make private cache scope observable with real data. No catalog/cart/checkout/payment/Admin/report/notification UI. [ADR-027](adr/0027-react-session-foundations.md) records this scope and cache/security contract. Sites local-only guidance was used for design/preview; user instructions prohibit publication. No Site registration, source synchronization or hosting/deployment tool was used.

Pinned **Node 24.19.0/npm 11.17.0**, React/DOM **19.3.1**, TypeScript **7.0.2**, Vite **8.3.8**, React plugin **6.3.0**, Router **8.5.1**, TanStack Query **5.107.1**, Zustand **5.1.0**. Stable npm metadata/peer engines checked; no preview package. Vitest **5.1.0**, Playwright **1.65.0**, Prettier **3.10.1** are dev tools; exact type packages/graph in client/package.json + lockfile. Core .NET SDK/packages/migrations unchanged.

### Completed acceptance

| Requirement | Behavior / evidence |
| --- | --- |
| Readable responsive app | Overview/login/register/current-user/read-only orders, real loading/empty/error/retry/404/role-denied states, sidebar/mobile drawer, labels/hints/server field messages, skip link, focus trap/Escape/return focus and reduced motion |
| Same-origin API | Relative /api paths + Vite dev proxy, Identity HttpOnly cookies, no CORS/JWT/cookie-domain bypass; real Chromium requests through proxy into Kestrel/PostgreSQL |
| Typed fetch / errors | HTTP status checked first, ProblemDetails code/trace/errors retained, header trace fallback for malformed/non-JSON failures, real 204 accepted, abort propagated, redirects/unexpected success JSON refused |
| Money | DTO strings, exact bigint cents, explicit scale/range validation/formatting; no Number totals/client payment authority |
| CSRF identity lifecycle | In-memory token retrieval/header, dedup + generation fence, refresh after login/logout cookie changes; registration never becomes login; rejected POST is not automatically replayed; next explicit attempt retrieves fresh token |
| Stable server cache | One QueryClient; factories/queryOptions include every order page/pageSize/status and current UserId; private unknown-identity bootstrap key isolated in SessionController; no persisted query cache |
| Logout / switch isolation | Hide/cancel/remove private data before request; scope/generation fences ignore old data and old 401s; failed logout re-verifies surviving cookie rather than false success; owner IDs checked before caching |
| Cross-tab / restore | Opaque BroadcastChannel signals, hidden/pagehide purge and focus/persisted-page revalidation; actual two-tab switch/logout checked; backend still authorizes all requests |
| Zustand scope | Versioned density only persisted, mobile drawer not persisted, unknown fields discarded; no auth/user/role/token/order/price data. Crafted Admin role does not bypass server 403 |
| Browser acceptance | Actual registration/server validation, cookie restore/missing/expired session, CSRF rejection/fresh retry, same-SPA account switch/logout, two tabs, unavailable restore, deterministic loading/empty/transport error/retry, keyboard and 320/375/768/1440px navigation |

### Luồng chính (tiếng Việt)

Vite phục vụ React và chuyển /api sang backend trên cùng origin mà browser nhìn thấy. Fetch client gửi cookie HttpOnly tự động, kiểm tra status trước khi đọc dữ liệu và giữ ProblemDetails code/traceId/errors. Lệnh thay đổi dữ liệu lấy request token CSRF trong RAM rồi gửi đúng header; không tự replay POST khi CSRF lỗi. Registration tạo Customer nhưng không đăng nhập; chỉ /me xác nhận cookie hiện tại.

SessionController điều phối trạng thái restoring/changing/anonymous/authenticated/unavailable. Trước login đổi tài khoản/logout, nó ẩn nội dung bảo vệ, tăng generation, cancel và xóa private cache. Request cũ trả về sau đó, kể cả 401, không được làm bẩn hoặc đăng xuất nhầm tài khoản mới. Chỉ 401 của scope hiện tại mới kết thúc session trên UI; lỗi mạng/500 là unavailable, không giả thành guest hoặc bảng rỗng. Query orders có key chứa UserId và tất cả filter/page; backend vẫn lọc owner và quyết định quyền.

Zustand chỉ giữ UI density/mobile menu; localStorage chỉ có density đã whitelist. Không lưu role, user, password, auth/CSRF token hay query data. Hai tab trao tín hiệu đổi identity không chứa dữ liệu riêng; khi ẩn/quay lại trang thì private state được xóa/xác minh lại. UI role chỉ là điều hướng, không phải quyền. Money là chuỗi; format/tính exact cents bằng bigint, totals vẫn lấy từ server.

### Important files / migration

- client/package.json, package-lock.json, .nvmrc, tsconfig.json, vite.config.ts: reproducible supported tooling and same-origin dev proxy.
- client/src/api/client.ts, types.ts, money.ts, queries.ts: typed status/error/CSRF transport, exact money and scoped queryOptions/key factories.
- client/src/auth/session.ts, context.tsx, returnTo.ts: verified-cookie session/privacy lifecycle and safe return routes.
- client/src/state/preferences.ts: whitelisted UI persistence; App/main/components/pages/styles: responsive real-data interface.
- client/tests and e2e/session.spec.ts: transport/session race rules plus real browser acceptance; playwright config disables credential-bearing traces/video.
- IntegrationTests/Frontend/FrontendBrowserTests.cs: isolated real Kestrel/DB/Node/Vite/Chromium harness, generated identities, test-only nonce clock control; BrowserControlAbsenceTests.cs proves that control is absent from normal API. Existing RealtimeApiHost.cs gets one test-only category log-level override so its post-ACK observation is independent of referenced appsettings output collisions; no production realtime change.
- [Frontend runbook](frontend.md), [ADR-027](adr/0027-react-session-foundations.md), architecture/spec/status/testing/authentication/README and ADR index. **No new database migration**, backend production source or .NET dependency changes.

### Exact checks and results

Tooling: existing Node 24.19.0/npm 11.17.0; DOTNET_CLI_HOME=/tmp/orderflow-frontend/dotnet-home, DOTNET_CLI_TELEMETRY_OPTOUT=1 for .NET. Core SDK 10.0.115/runtime 10.0.15/package pins unchanged. Browser/test artifacts under /tmp/orderflow-frontend; npm output ignored under client/node_modules/dist. Test identities/passwords/control nonce are generated per case and passed through child process environment, never command arguments/source/logs. No real credentials, developer migration/reset or deployment.

1. Checked stable official npm versions and peer engines before install. Sites local-only installer installed pinned packages; added pinned dev-only Prettier for readable source. Final `npm ci` in client — **exit 0**, **56 packages added / 57 audited, 0 vulnerabilities**. Package/lock inputs are reproducible; no suppressed audit result.
2. `npm --prefix client run build` — **exit 0**, TypeScript strict check + Vite production build. Final observed build **157 modules**, JS **327.40 kB / 102.10 kB gzip**, CSS **19.52 kB / 5.28 kB gzip**, ~230ms Vite stage; this is a local build artifact size/time, not a production performance claim. Initial TypeScript checks exposed a discriminated-union narrowing and unknown test-error type; corrected both before final checks.
3. `npm --prefix client run test` — **exit 0**, final **35 Vitest cases passed**, ~145ms. Includes status/ProblemDetails/trace/fields/non-JSON/204, fresh CSRF retry/generation fencing, no cross-origin cookie paths, exact bigint cents/overflow, all query-key params/scope, safe route return, pre-login private purge, ignored-abort old data, stale 401, failed logout, unavailable restore and registration not becoming session.
4. `npm --prefix client run format:check` — **exit 0**, all matched TS/TSX/CSS/JSON/HTML formatted. No unrelated linter/package/framework added. A pinned formatter keeps source readable rather than leaving generated/minified JSX/CSS in source.
5. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-frontend/artifacts -m:1` — **exit 0**, six projects restored with unchanged locks. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-frontend/artifacts -m:1` — **exit 0**, final **0 warnings/errors**, **5.37s**. No production .NET model/package/source change.
6. `npm --prefix client exec playwright install chromium` — **exit 0**, matching Playwright Chromium/headless shell **v1254 / Chrome for Testing 158.0.8127.18** installed; actual browser execution succeeded, not inferred from installation.
7. Initial `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-frontend/artifacts -m:1 --filter 'FullyQualifiedName~FrontendBrowserTests|FullyQualifiedName~BrowserControlAbsenceTests' --logger 'trx;LogFilePrefix=frontend-browser-initial' --results-directory /tmp/orderflow-frontend/results` — **9 passed / 1 responsive failure**. Fixed resize checking to wait for actual measured reflow, then fixed Escape return focus to wait until React removed workspace inert. Focused mobile run then **1 passed**, with no arbitrary sleep. Browser proof now checks 320/375/768/1440 widths and drawer focus/keyboard behavior.
8. Expanded auth/browser command with filter `FullyQualifiedName~FrontendBrowserTests|FullyQualifiedName~BrowserControlAbsenceTests|FullyQualifiedName~AuthenticationTests|FullyQualifiedName~AuthCompositionTests`, prefix **frontend-browser-final** — **28 passed, 0 failed/skipped**, **38s**. Includes all nine actual browser scenarios plus control absence and existing auth regressions. Added error-generation fencing after review; latest browser/combined verification is recorded below.
9. Full command `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-frontend/artifacts -m:1 --logger 'trx;LogFilePrefix=frontend-full' --results-directory /tmp/orderflow-frontend/results` — initially **454 integration passed / 1 failed**, plus **178 unit passed**, no skips; integration **5m51s**. All frontend cases passed. The old realtime test received client hints but timed out waiting for safe post-ACK Information event 2403. Isolated recheck reproduced it. Inspected fresh integration output: Simulator appsettings (Default=Warning) had won the shared appsettings copy; earlier reporting artifacts had API appsettings (Default=Information). Added one explicit test-host category level for RealtimeDeliveryHandler so the existing deterministic observation is not filtered. Did not loosen assertions, extend waits or alter realtime production logic.
10. Local preview: owned API/Vite on loopback 5080/5173, HTTP health/anonymous /me through the actual proxy, preview document **200**. Native open-in-Codex returned queued for this chat; also queued the durable screenshot. Visually inspected real desktop overview/account and mobile login screenshots. Owned preview services were stopped cleanly after QA; no developer data was queried/seeded/migrated by preview. Test screenshots remain under `/tmp/orderflow-frontend/browser-artifacts`; traces/video are disabled.
11. After the test-host log fix and final heading-focus improvement, reran build/test/format checks successfully, then the combined filter `FullyQualifiedName~FrontendBrowserTests|FullyQualifiedName~BrowserControlAbsenceTests|FullyQualifiedName~Two_API_instances_Rabbit_on_A_reaches_owner_connections_on_B_through_real_Redis_with_minimal_duplicate_hints`, prefix **frontend-fixed** — **11 passed, 0 failed/skipped**. TRX `frontend-fixed_net10.0_20261113170636.trx`; the unit project correctly matched zero tests. The actual two-node RabbitMQ/Redis test retains its existing post-ACK assertion.
12. Final full suite — command from (9), prefix **frontend-verified**, with the stated DOTNET_CLI_HOME/telemetry environment — **exit 0**, **455 integration + 178 unit = 633 passed, 0 failed/skipped**; integration **5m9s**, unit **57ms**. TRX `frontend-verified_net10.0_20261113171527.trx` and `frontend-verified_net10.0_20261113171528.trx` under `/tmp/orderflow-frontend/results`. An initial sandbox-only launch aborted before executing tests because VSTest could not open its localhost socket (Permission denied); the approved run with local socket/container access produced these passing results. No production/test source changed after this full passing run; only documentation was finalized.
13. `git diff --check` — **exit 0**. Final static text check covers **43 changed/new files**, valid JSON, balanced Markdown fences, conflict markers and **324 existing relative Markdown links**; no issue. README now reflects the existing actual SDK pin and new browser prerequisites instead of its stale pre-frontend instructions. New frontend packages/lock are explicit; .NET packages/model/migrations remain unchanged. No commit, push, deployment or developer DB migration/reset.

### Manual verification / trade-offs / remaining work

Follow [frontend runbook](frontend.md): privately configure/migrate DB and seed roles using existing explicit commands; start API on loopback 5080 with the existing Development-only HTTP opt-in, npm ci + npm run dev in client, then open the printed URL. Register, sign in, reload Account, view real Orders, switch using SPA navigation, sign out/Back, repeat across tabs, and try failed/expired CSRF/auth/network cases. Look at storage (density only) and HttpOnly cookies. Backend remains authoritative; no fake successful API responses or default login credentials.

Readonly orders are a cache-isolation prerequisite, not completed checkout UI. Current order data can be stale until normal refetch; it never authorizes inventory/payment actions. Individual frames in a suspended/unresponsive tab cannot be erased instantly by another tab; supported app transitions signal/purge/revalidate, and the server validates every request. External/manual cookie changes are detected on focus/restore, active-scope 401 or response owner mismatch. No continuous cross-device revocation, Safari/Firefox/full WCAG audit, production TLS/proxy/CSP/shared keys/SPA fallback, load benchmark, catalog/cart/checkout/Admin/report/notification UI or Site deployment is claimed.

### Three interview questions

1. **Vì sao HttpOnly cookie vẫn cần CSRF và phải đổi token sau login/logout?** Hiểu cookie được browser gửi tự động, request token gắn danh tính hiện tại; registration không tự đăng nhập và token cũ không được dùng cho tài khoản mới.
2. **Vì sao key có UserId vẫn phải cancel/xóa/fence?** Hiểu private data cũ không được hiện trong lúc đổi identity; AbortSignal không đủ nếu response muộn vẫn về, kể cả 401. Backend owner SQL mới là security boundary.
3. **TanStack Query khác Zustand ở đâu?** Query giữ server state có scope/lifetime, Zustand chỉ UI preference. Không persist role/auth/query data; money giữ string/bigint cents và tổng tiền thuộc backend.

Suggested commit title (none supplied): `feat(client): add React session UI and scoped query caching`.


## Milestone 20 — customer browsing and browser cart (2026-11-16)

### Inspection and scope

Inspected AGENTS.md, clean main/origin Git status, spec/architecture/status/ADRs, existing React/session/query/money/layout/tests, pinned Node/npm/.NET/packages/locks, public/admin Catalog DTOs/sorts, checkout quantity/line limits, migration chain and the real PostgreSQL browser harness before edits. No existing cart to preserve or migrate. Latest DB migration remains 20261113082834_AddReportingReadIndexes (13 migrations); this milestone adds none. Existing deterministic server sorts/visibility/stock reads and checkout transaction authority are reused. ADR-027's previously deferred catalog/cart is now implemented by [ADR-028](adr/0028-catalog-cart.md), with no conflict in auth/private-cache invariants.

Scope is milestone 20: customer/anonymous public browsing/detail, filters/sort/paging and cart intent/review. Actual order submission/uncertain-key recovery/payment/cancel/detail actions remain 21. No production backend, worker, authorization, CORS, database model/migration, package/lockfile or SDK changes. Node 24.19.0/npm 11.17.0 and all exact React/TypeScript/Vite/TanStack Query/Zustand/Vitest/Playwright pins are preserved. Official TanStack pagination/cancellation and Zustand persist version/migrate/merge documentation was consulted. Sites skill applied local-only to preserve current design; no Site registration/source synchronization/publication. No agents, commits, pushes, deployment/history rewrite or developer-data deletion/reset.

### Acceptance delivered

| Requirement | Evidence / behavior |
| --- | --- |
| Product browsing/detail | `/products` and `/products/:id`, live API cards/detail/category labels/string money/advisory Available; explicit 404/archived/stock-zero/error/retry/loading/empty states |
| Filters/deterministic pagination | URL/keys include q/categoryId/sort/page/pageSize; 300ms debounced search, filter changes reset page one; server UUID tie-breaker retained; repeated equal-price pages have no duplicates for a fixed fixture |
| Bounded category choices | Category q/sort/page/pageSize keys, groups of 20 with Previous/Next; browser selects Travel from second category page |
| Cancellation / page UX | Query AbortSignal reaches fetch; real QueryClient cancellation test waits for actual transport arrival before cancel. No previous-page/filter placeholders; gated page-two browser request verifies loading and zero old cards |
| Immutable bounded actions | Add merges, Update replaces, Remove/Clear explicit; 100 distinct products, integers 1–1000, valid canonical UUIDs; invalid/boundary inputs leave cart unchanged |
| Persistence / migration | Version-1 IDs/quantities only, <=16 KiB UTF-8 read/100 inspected rows, runtime sanitization/duplicate policy, unknown versions reset, corrupt/denied storage notice + safe constant warning, reload restore |
| Exact estimated money | Query product prices -> bigint cents -> numeric(18,2)-bounded strings; 3×0.10 + 0.20 = 0.50; updated 3×0.15 = 0.45; beyond-Number precision/overflow/missing-price tests |
| Advisory review | Deliberate detail refetch, changed-price messages, current totals, review signature + late-result fence; errors/missing items/shortage/overflow withhold valid review; unavailable row remains removable |
| No reservation fiction | Cart add/update never changes Available; server remains at 100 in browser proof. No order POST, fake checkout success or optimistic stock write |
| Account and tab scope | Visible shared-browser cart policy; IDs/quantities persist across A/logout/B. Existing private-order isolation still passes. Storage events sync another tab; concurrent edits are documented last-write-wins |
| Responsive/accessibility | Labeled quantity forms, explicit submit/update/remove/clear, inline alert errors, status announcements, category pressed states/paging, loading/error retry and cart summary; browser widths 320/375/768/1440 plus real desktop/mobile screenshot inspection |
| Actual browser/API integration | Seven new shopping flows augment nine auth/session flows; real PostgreSQL/Kestrel/Vite/Identity/CSRF, Admin price/archive changes and real network aborts. No intercepted fabricated successful response |

### Luồng chính (tiếng Việt)

Trang Products đọc bộ lọc từ URL, tạo queryOptions/key chứa đủ tham số rồi gọi API công khai qua proxy. Search đợi 300 ms khi gõ, hoặc submit ngay; đổi filter/sort/pageSize về trang một. Category có phân trang riêng. Khi key đổi, React hiển thị loading thay vì gắn nhãn trang mới lên dữ liệu cũ. API quyết định thứ tự và visibility; UUID là tie-breaker khi dữ liệu không đổi.

Add/Update chỉ thay mảng ID/số lượng trong Zustand bằng immutable updates. Cart version 1 chỉ ghi hai trường này vào localStorage; hydration kiểm tra kích thước, kiểu và giới hạn, bỏ trường lạ, không đọc giá/role/tổng tiền từ storage. Đây là giỏ của trình duyệt được giữ qua login/logout/đổi tài khoản, đã ghi rõ trên trang. Query dữ liệu riêng của account vẫn bị purge/fence theo ADR-027. Các tab đọc lại cart khi nhận storage event; không hứa merge đồng thời.

Cart dùng Query lấy lại từng product detail, tính ước tính bằng bigint cents và báo dòng thiếu giá/404/hết hàng/thiếu Available. Nút Check prices & availability refetch thật, so giá đang hiển thị với response để báo thay đổi và tính lại tổng; thay quantity/dữ liệu hoặc lỗi sẽ làm review cũ hết hiệu lực. Giỏ không tạo order và không giữ kho. Cache server tùy chọn có thể trả catalog fields cũ tới 30 giây tại lookup; Available được đọc riêng. Khi tạo order, PostgreSQL mới khóa/revalidate và chốt snapshot giá/tồn kho/tổng tiền.

### Important files / migration

- client/src/api/catalog.ts, types.ts: public query keys/options, complete parameters/AbortSignal, product DTO checks and identity validation.
- client/src/catalog/parameters.ts; pages/Catalog.tsx, ProductDetail.tsx: URL normalization, debounced keyword, bounded category choices, deterministic server paging/detail and graceful unavailability.
- client/src/state/cart.ts: immutable bounded actions, validated versioned whitelisted persistence, storage-failure behavior; main.tsx: storage-event synchronization/HMR cleanup.
- client/src/cart/estimate.ts, components/QuantityForm.tsx, pages/Cart.tsx: bigint estimates/availability rules, accessible validation, refetch/review/message/race lifecycle and responsive summary.
- App/Layout/Home/ui/styles, auth/returnTo.ts: navigation/icons/routes/design and safe `/cart` sign-in return; existing session coordinator/private queries unchanged.
- client/tests/cart.test.ts, catalog.test.ts; client/e2e/shopping.spec.ts; IntegrationTests/Frontend/FrontendBrowserTests.cs: real QueryClient/action/persistence rules and seven additional actual-browser scenarios/conditional owned fixtures/Admin credentials passed only through environment.
- [ADR-028](adr/0028-catalog-cart.md), [frontend runbook](frontend.md), architecture/spec/testing/status/ADR index and READMEs updated. **No new migration or dependency/lock change.**

### Exact checks and results

Artifacts: `/tmp/orderflow-shopping/artifacts`, `/tmp/orderflow-shopping/results`, `/tmp/orderflow-shopping/browser-artifacts`. .NET environment: `DOTNET_CLI_HOME=/tmp/orderflow-shopping/dotnet-home DOTNET_CLI_TELEMETRY_OPTOUT=1`; actual installed SDK 10.0.115. Browser binaries already installed at matching Playwright Chromium/headless-shell v1254; real executions below succeeded. Existing npm dependencies were available with unchanged input/lock, so no unnecessary reinstall/update. All generated fixtures/control nonce/passwords stay in owned test DB/process environment, not source/command arguments; traces/video remain off.

1. `dotnet restore OrderFlow.slnx --locked-mode --artifacts-path /tmp/orderflow-shopping/artifacts -m:1` — initial restricted-network attempt failed NU1301 (NuGet socket Permission denied); approved network rerun **exit 0**, six projects restored, last project **35.23s**, no version/lock change. Not a fake/ignored successful restore.
2. `dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path /tmp/orderflow-shopping/artifacts -m:1` — **exit 0**, **0 warnings/errors**, **16.67s**. Only .NET source edit is the existing browser test harness/fixtures.
3. `npm --prefix client run build` — **exit 0**, strict TypeScript + Vite, **165 modules**, JS **353.55 kB / 108.74 kB gzip**, CSS **26.96 kB / 6.70 kB gzip**, local Vite stage ~173ms. This is an observed local build, not a deployed performance claim.
4. `npm --prefix client run test` — **69 Vitest cases passed** across four files. Includes all prior 35 cases plus new cart/catalog cases: immutable actions/bounds, text quantity validation, scoped complete keys/URL parameters, awaited transport arrival/AbortSignal cancellation, reload/whitelist/unsupported version/duplicates/corrupt or unavailable storage/UTF-8 bounds/cross-store hydration, exact money/overflow/unknown price and advisory stock.
5. `npm --prefix client run format:check` — **exit 0**, all matched source/test/JSON/HTML formatted. Source remains readable with the existing pinned formatter; no UI/test library was added.
6. Initial actual-browser command: `dotnet test OrderFlow.slnx -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-shopping/artifacts -m:1 --filter 'FullyQualifiedName~FrontendBrowserTests|FullyQualifiedName~BrowserControlAbsenceTests' --logger 'trx;LogFilePrefix=shopping-browser-initial' --results-directory /tmp/orderflow-shopping/results` — **17 integration passed, 0 failed/skipped**, **1m8s**; unit project matched zero by design. TRX `shopping-browser-initial_net10.0_20261116141709.trx` / `...141710.trx`. This includes all sixteen actual Chromium scenarios and the normal-API control-absence check.
7. Expanded final relevant regression: `dotnet test tests/OrderFlow.IntegrationTests/OrderFlow.IntegrationTests.csproj -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-shopping/artifacts -m:1 --filter 'FullyQualifiedName~Frontend|FullyQualifiedName~Catalog|FullyQualifiedName~Checkout|FullyQualifiedName~AuthenticationTests' --logger 'trx;LogFilePrefix=shopping-verified' --results-directory /tmp/orderflow-shopping/results` — **113 integration passed, 0 failed/skipped**, **2m3s**. TRX `shopping-verified_net10.0_20261116142410.trx`. Real PostgreSQL/Redis/catalog-cache/checkout/auth regressions and all sixteen browser scenarios pass. Final catalog screenshots also assert no horizontal overflow at 320/375/768 and were visually inspected with cart desktop/mobile; fixture imagery uses inline icons because no product-image API exists.
8. `dotnet test tests/OrderFlow.UnitTests/OrderFlow.UnitTests.csproj -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-shopping/artifacts -m:1 --logger 'trx;LogFilePrefix=shopping-unit' --results-directory /tmp/orderflow-shopping/results` — **178 passed, 0 failed/skipped**, **144ms**. TRX `shopping-unit_net10.0_20261116142203.trx`. Full unrelated integration suites were not repeated for this frontend-only milestone; do not substitute the prior milestone's full-suite total for a current run.
9. Final storage hardening adds an explicit UTF-8 byte check after the bounded character check, so multibyte hostile input cannot exceed the documented 16 KiB parse limit. After that source change, format/build/Vitest from (3–5) all **exit 0**, **69 passed** in **180ms**; final bundle JS **353.60 kB / 108.77 kB gzip**, CSS unchanged, Vite **237ms**. Focused final `dotnet test tests/OrderFlow.IntegrationTests/OrderFlow.IntegrationTests.csproj -c Release --no-build --no-restore --artifacts-path /tmp/orderflow-shopping/artifacts -m:1 --filter 'FullyQualifiedName~FrontendBrowserTests&DisplayName~cart_quantities_restore' --logger 'trx;LogFilePrefix=shopping-cart-final' --results-directory /tmp/orderflow-shopping/results` — **1 passed**, **5s**, TRX `shopping-cart-final_net10.0_20261116142438.trx`. No production/test source changed after these passing checks; only documentation was finalized.
10. Visually inspected real synthetic-fixture catalog/cart desktop/mobile screenshots; native open-in-Codex for the cart image returned queued, not a deployment/preview-server claim. Every test-owned Kestrel/Vite/Node/browser/DB is torn down by the harness; no persistent preview or developer DB was configured. Static text/diff verification is recorded below.
11. `git diff --check` — **exit 0**. Static validation covered **29 changed/new text files** and **328 relative Markdown links**, JSON, balanced fences and conflict markers: **all passed**. Changed package manifests/locks/SDK pins/migrations: **none**. No commit/push/deployment or user-data migration/reset.

### Manual verification / remaining trade-offs

Follow [frontend setup and shopping acceptance](frontend.md). Configure/migrate the existing private dev DB and provision roles through its existing explicit commands, start API on loopback 5080 with Development HTTP opt-in, then `npm --prefix client run dev`. Open `/products`, exercise keyword/category/sort/page/pageSize and Back/reload; add products, update/invalid quantity, reload `/cart`, Remove/Clear and compare exact estimates. Use a separate real Admin API session to change a selected price/archive a product or category, then Check in cart. Stop/block a detail request and Retry. Verify Available is unchanged by cart actions. Repeat across account switches and two same-origin tabs; try 320/375/768/1440px and keyboard controls. There is no default credential/fake successful API/demo catalog; browser fixtures are test-only.

The cart intentionally survives sign-out on the same device; clear it on shared computers. Simultaneous tab writes are last-write-wins. Cart detail fan-out is bounded to 100 GETs but has not been load/latency benchmarked. Server offset pages can move under concurrent writes; cached catalog fields can be up to 30s old at a fresh lookup and displayed values can age further afterward. Review is not an atomic quote/hold or checkout action. Existing PostgreSQL order creation owns active-product/price/stock decisions. Actual checkout/recovery/payment/Admin/report/notification UI, cross-device carts, Safari/Firefox, full WCAG/screen-reader audit, production TLS/proxy/CSP/deployment and unrelated full integration suites remain unverified/deferred for this milestone.

### Three interview questions

1. **Vì sao query key phải có đủ filter/page và vẫn cần AbortSignal?** Hiểu key chọn đúng server-state snapshot, còn abort tránh request cũ tốn tài nguyên/chen UI; không dùng placeholder trang cũ mà thiếu nhãn loading.
2. **Vì sao cart chỉ persist ID/quantity và dùng bigint cents?** Hiểu giá/tồn kho thuộc Query/API, storage là input không tin cậy; 0.10 × 3 phải ra 0.30 chính xác và lỗi/overflow không được đổi thành 0.
3. **Review cart khác reserve/checkout thế nào?** Hiểu cache/read có staleness và race; chỉ transaction PostgreSQL tạo order mới khóa/revalidate và chốt immutable prices/available stock. Giỏ cấp trình duyệt giữ qua đổi account là policy rõ ràng, không phải quyền hay dữ liệu order riêng.

Suggested commit title (none supplied): `feat(client): add customer catalog browsing and persistent cart`.
