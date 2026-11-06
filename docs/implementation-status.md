# OrderFlow implementation status

Updated: 2026-11-06 (Asia/Ho_Chi_Minh). Current milestone: **15 / submission foundations of 16 — durable payment initiation and restricted separate simulator host**. Authenticated owner initiation/current reads, one unresolved attempt/order, original 202 replay, server money/provider identity, lease-fenced submission/reconciliation, independent provider persistence, signed callback generation and provider-side idempotent full refunds are implemented. Order confirmation/consumption, callback receipt/settlement, merchant compensation, remaining dispatch/inbox 14, UI and deployment remain pending.

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
| 05 | React/TypeScript skeleton; pinned client tooling, TanStack Query/Zustand, dev same-origin proxy | 02, 02a | A01 | Pending |
| 06 | Authorization/ownership contracts/policies/handlers, denial mapping and focused CSRF/auth/resource tests | 04 | A02 | Complete: representative-resource foundations; real Order integration remains 13/19 |
| 07 | Products/Categories domain/schema, Admin create/update/archive, public read/search/paging, exact prices and concurrency | 03, 06 | A03 | Complete: Catalog slice; Order snapshots/totals remain 11/12 |
| 08 | Inventory balances/append-only movements, zero initialization/backfill, Admin receipts/adjustments/replay and stock races | 07 | A04, A16 | Complete: stock foundation; reservations deferred to 09 |
| 08a | Reusable PostgreSQL/WebApplicationFactory harness, per-case isolation, logical time and safe race boundaries | 08 | A02, A04, A16, A23 (current slices) | Complete: 238 tests; PostgreSQL subset repeated; no new business feature |
| 09 | Reservation model/behavior and necessary Order identity/FK prerequisites; SQL/domain/race tests | 08 | A04, A07, A08, A16 | Active create/reserve delivered in 12; release/terminal guards in 13; consumption remains with capture integration |
| 10 | Operation-specific normalization, replay/conflict protocol using the shared idempotency store | 03, 06 | A06 | CreateOrder:v1 normalization/replay/conflict delivered with 12; later operations retain separate identities |
| 11 | Complete Order/item snapshots/lifecycle and transactional events on reservation/Order foundations | 07, 09 | A03, A07, A08 | Creation delivered in 12; cancellation/expiry and fulfillment/history in 13; confirmation/payment integration remains 18 |
| 12 | Atomic CreateOrder/reserve + original response; stock/idempotency concurrency and rollback | 08a; checkout portions of 09–11 | A05, A06, A08 creation/replay, A16, A23 | Complete for requested checkout; see evidence and remaining lifecycle work below |
| 13 | Own order reads/list, pending cancellation/release, requested Admin fulfillment and authorization/race regressions | 12 | A02, A07 release, A08, A09, A15 graph | Complete for requested scope; automatic expiry and payment/capture integration remain later |
| 14 | Outbox dispatch/inbox and worker lease/retry foundation; expiry worker + deterministic races | 13 | A07–A09, A16–A19 | Expiry slice complete: bounded SKIP LOCKED worker + deterministic races; dispatch/inbox remain pending |
| 15 | Payment attempt/intention schema, request idempotency, active-attempt constraint, customer start/read | 10; expiry/worker foundations of 14 | A12, A13, A23 | Complete for requested initiation scope; verified settlement remains 17/18 |
| 16 | Durable gateway simulator, submission/query worker, independent transaction boundary and uncertainty | 15 | A12, A13, A19, A23 | Provider/submission/reconciliation foundations complete; callback delivery/receipt and merchant compensation remain later |
| 17 | Signed callback receipt/inbox, verification, duplicate/quarantine handling and replay tests | 16 | A13, A14 | Pending |
| 18 | Atomic capture confirmation/stock consumption; late capture compensation/refund reconciliation and races | 17 | A07–A13, A16, A17, A19, A23 | Pending |
| 19 | Admin fulfillment state machine, expected versions, order inspection and authorization | 13; verified payment integration 18 | A02, A15 | Requested list/detail/graph/expectedVersion APIs delivered in 13; verified payment-confirmed end-to-end integration remains 18/19 |
| 20 | Browser session/CSRF flow, active catalog, cart and exact cents state | 05, 06, 07 | A03, A20 | Pending |
| 21 | Checkout UI, pending key/body recovery, authoritative order/deadline/payment/cancel views | 18, 20 | A03, A20 | Pending |
| 22 | Admin catalog/stock/order/fulfillment UI with state/version conflict handling | 09, 19, 20 | A20 | Pending |
| 23 | Deduplicated local notifications consumer, durable deliveries and failure visibility | 14, 18 | A17, A18, A21 | Pending |
| 24 | Versioned reporting projection, daily winning sales, range/auth tests and report UI | 19, 20, 23 | A17, A18, A21 | Pending |
| 25 | Reproducible API/client/worker/proxy local composition, migration command, runtime DB roles and readiness | 22, 24 | A01, A16, A22 | Pending |
| 26 | Deployment hardening/shared protected Data Protection keys; multi-worker/crash/uncertain commit/stale lease fault suite; retry/no-network-in-transaction evidence | 04, 18, 23, 24, 25 | A02, A12, A17–A19, A22, A23 | Pending |
| 27 | Browser acceptance flows; cross-user/CSRF/auth regressions; structured redaction/config checks | 21, 22, 25, 26 | A02, A20, A22 | Pending |
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
