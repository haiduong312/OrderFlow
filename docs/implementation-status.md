# OrderFlow implementation status

Updated: 2026-11-02 (Asia/Ho_Chi_Minh). Current milestone: **03 — PostgreSQL persistence and reliability storage**. Scoped Identity-compatible context, initial migration, shared idempotency/outbox/inbox schema, PostgreSQL-only Compose profile and explicit roles-only dev seed are implemented/tested. No auth endpoint, frontend, catalog/order/payment behavior or broker/lease processor exists yet. A01/A03/A06/A08/A16–A19/A22 remain partial beyond the evidence below.

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
| 04 | Customer browser auth endpoints/cookies, required CSRF and safe Admin bootstrap using existing Identity storage | 03 | A02 | Pending |
| 05 | React/TypeScript skeleton; pinned client tooling, TanStack Query/Zustand, dev same-origin proxy | 02, 02a | A01 | Pending |
| 06 | Authorization/ownership conventions, CSRF/auth regressions and HTTP test helpers | 04 | A02 | Pending |
| 07 | Catalog domain/admin CRUD/deactivation; exact money JSON, versions, product snapshots | 03, 06 | A03 | Pending |
| 08 | Inventory balances/history schema, guarded adjustments and ledger constraints; reservation domain rules | 07 | A04, A07, A16 | Pending |
| 09 | Admin stock HTTP endpoints, operation replay, stock races and constraint tests | 08 | A04 | Pending |
| 10 | Operation-specific normalization, replay/conflict protocol using the shared idempotency store | 03, 06 | A06 | Pending |
| 11 | Order/line/reservation schema/domain, deadline rules, transactional order events and state tests | 07, 08 | A03, A07, A08 | Pending |
| 12 | Atomic CreateOrder/reserve + original response; stock/idempotency concurrency and rollback | 09, 10, 11 | A05–A08, A16, A23 | Pending |
| 13 | Own order reads/list, pending cancellation/release and authorization regressions | 12 | A02, A07–A09 | Pending |
| 14 | Outbox dispatch/inbox and worker lease/retry foundation; expiry worker + deterministic races | 13 | A07–A09, A16–A19 | Pending |
| 15 | Payment attempt/intention schema, request idempotency, active-attempt constraint, customer start/read | 10, 14 | A12, A13, A23 | Pending |
| 16 | Durable gateway simulator, submission/query worker, independent transaction boundary and uncertainty | 15 | A12, A13, A19, A23 | Pending |
| 17 | Signed callback receipt/inbox, verification, duplicate/quarantine handling and replay tests | 16 | A13, A14 | Pending |
| 18 | Atomic capture confirmation/stock consumption; late capture compensation/refund reconciliation and races | 17 | A07–A13, A16, A17, A19, A23 | Pending |
| 19 | Admin fulfillment state machine, expected versions, order inspection and authorization | 13, 18 | A02, A15 | Pending |
| 20 | Browser session/CSRF flow, active catalog, cart and exact cents state | 05, 06, 07 | A03, A20 | Pending |
| 21 | Checkout UI, pending key/body recovery, authoritative order/deadline/payment/cancel views | 18, 20 | A03, A20 | Pending |
| 22 | Admin catalog/stock/order/fulfillment UI with state/version conflict handling | 09, 19, 20 | A20 | Pending |
| 23 | Deduplicated local notifications consumer, durable deliveries and failure visibility | 14, 18 | A17, A18, A21 | Pending |
| 24 | Versioned reporting projection, daily winning sales, range/auth tests and report UI | 19, 20, 23 | A17, A18, A21 | Pending |
| 25 | Reproducible API/client/worker/proxy local composition, migration command, runtime DB roles and readiness | 22, 24 | A01, A16, A22 | Pending |
| 26 | Multi-worker/crash/uncertain commit/stale lease fault suite; retry and no-network-in-transaction evidence | 18, 23, 24, 25 | A12, A17–A19, A23 | Pending |
| 27 | Browser acceptance flows; cross-user/CSRF/auth regressions; structured redaction/config checks | 21, 22, 25, 26 | A02, A20, A22 | Pending |
| 28 | Operational backlog/failure view, health/lag metrics, audited same-identity operator replay and recovery runbook | 25, 26, 27 | A19, A22 | Pending |
| 29 | Full acceptance walkthrough; portfolio architecture/demo/interview guide and verified limitations | 28 | A01–A24 | Pending |

Feature order is intentional: HTTP idempotency/stock correctness precede payment; outbox/worker durability precede external intentions; auth/CSRF precede protected client flows; fault tests and operability precede the final demo. Reservation/order foreign keys are introduced together in 11; command-to-order links can be completed in 12 after both schemas exist. Reliability storage is now implemented in 03, ahead of its operation-specific use in 10/11/12 and dispatch in 14. Required CSRF protection ships with auth endpoints in 04; 06 extends authorization/regression coverage. Each feature milestone writes its relevant tests then; 26 does not defer concurrency proof. The latest instruction swaps the prior 04 React/05 Identity deliverables to 05/04; ADR-009 records that scope correction.

Requested supplemental milestone **02a** depends on 02 and is **Complete: shared HTTP/application foundations**. It establishes the common error/JSON/time/pagination conventions used by 03 and later HTTP features. IDs 01–29 and their business scopes are retained; 03 remains PostgreSQL work, not implicitly completed by this addition. ADR-008 records this roadmap adjustment and the clarified money input/output grammar.

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
