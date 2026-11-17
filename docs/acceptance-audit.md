# Repository acceptance audit — 2026-11-17

Scope: inspect shipped code/tests against [project-spec](project-spec.md), milestone requests and [status](implementation-status.md); repair material reproducibility gaps and label unfinished work. Inspection started on a clean main checkout; no user changes were overwritten. No push/publication is authorized. Supported dependency versions/locks and13 migrations were inspected; no package/SDK/schema upgrade is part of this audit.

The baseline specification contains historical forward-looking prose. Its current acceptance obligations and later ADRs must be read together. This audit explicitly leaves partial obligations partial rather than treating the milestone number or a green subset as full completion.

## Findings and disposition

| Finding | Disposition |
| --- | --- |
| Empty Data Protection ring previously rejected cookie/AF across cold replicas; primary quick start did not initialize it | **Fixed locally:** explicit non-serving API CLI `initialize-deployment-keys`, protected shared directory/application identity/certificate, one-off Compose tool before replicas. Repeat invocation verifies the established ring. New independent-provider regression and fresh real two-API browser rehearsal cover it. No arbitrary concurrent bootstrap, live rotation or production availability guarantee. |
| Fragmented initial setup/demo lacked safely seeded catalog/Customer and one sequence for payment/replay/recovery | **Fixed:** guarded fresh `.local` UUID demo, explicit migrate/bootstrap/keys/provider migration, real CSRF API fixtures and operator `pay`/`duplicate`/`outage` commands. Keys/payloads persist before uncertain writes. No real credentials/default password, fake UI response or production failpoint. |
| Spec endpoint list advertised generic operational failures; no controller maps it | **Incomplete/labeled:** route is deferred; only implemented email failure/retry and per-order audit are documented. No generic retry executor was added. |
| Original section7 promises standalone stock and refund-failed integration events | **Incomplete/labeled:** atomic stock+ledger and persisted refund NeedsReview states exist; standalone inventory/refund-failed outbox events do not. A16 is partial for that obligation. |
| Original order event contract requires confirmedAt/winning capture enrichment absent from `OrderEvents.Create` | **Incomplete/labeled:** minimal state snapshot is implemented; enrichment remains a contract gap. ADR-026 direct reports derive finances from immutable capture/item rows, not these events. |
| Stale prose says payment/expiry/report UI are unimplemented, reporting projection is planned, broker remains outside v1, and universal idempotency-first lock order | **Corrected:** historical sections labeled; current summaries/endpoint coverage/operation-specific ordering distinguish implemented behavior and deferred obligations. No invented ConfirmedAt property/reporting consumer. |
| Telemetry trace test assumed two publication calls always picked two source orders while a live consumer could enqueue a child at the same due timestamp | **Fixed test synchronization:** publish both original events to real durable queues before starting the consumer; child UUID ordering can no longer replace the retried source. No sleep/extra retry or production algorithm change. |
| CV/demo could imply production delivery or a cache improvement | **Bounded:** four English bullets with code/test map, simulator/local/remote-CI caveats, no benchmark numbers. Existing local report showed fewer SQL calls with worse HTTP latency; no performance gain/scale claim. |

## Acceptance map

“Covered” below means code and named tests exist, with final local execution recorded separately. It does not imply production certification or all future extensions.

| Criteria | Actual implementation / evidence | Audit state |
| --- | --- | --- |
|A01|Six-project pinned solution/locks; Vite client/proxy; multi-stage Compose; [deployment verification](../tests/OrderFlow.IntegrationTests/Deployment/DeploymentProfileTests.cs)|Covered locally; remote clean hosted environment separate |
|A02|[Identity/CSRF/roles](../src/OrderFlow.Api/Auth), [Auth tests](../tests/OrderFlow.IntegrationTests/Auth), owner-constrained order/payment/notification queries; session browser isolation|Covered; no public Admin role grant |
|A03–A04|[money/domain rules](../src/OrderFlow.Application/Common/Money), [Inventory](../src/OrderFlow.Application/Modules/Inventory), [SQL tests](../tests/OrderFlow.IntegrationTests/Inventory), bigint client totals|Covered exact USD/integer/ledger/version rules |
|A05–A06|[CheckoutService](../src/OrderFlow.Application/Modules/Ordering/CheckoutService.cs), [checkout SQL/HTTP tests](../tests/OrderFlow.IntegrationTests/Ordering), actual replica lock gate and stored bytes|Covered all-or-nothing/no oversell/replay; no InMemory evidence |
|A07–A09|[reservation/lifecycle/expiry](../src/OrderFlow.Application/Modules/Ordering), [InventoryReservation](../src/OrderFlow.Application/Modules/Inventory/InventoryReservation.cs), [ordering tests](../tests/OrderFlow.IntegrationTests/Ordering)|Covered state guards/equality/release once/owner cancellation |
|A10–A14|[settlement/recovery](../src/OrderFlow.Application/Modules/Payments), [callback tests](../tests/OrderFlow.IntegrationTests/Payments/PaymentCallbackTests.cs), [gateway](../tests/OrderFlow.IntegrationTests/Payments/PaymentGatewayTests.cs), [recovery](../tests/OrderFlow.IntegrationTests/Payments/PaymentRecoveryTests.cs)|Covered with independent simulator DB/real HTTP; no live provider |
|A15|[OrderTransitions](../src/OrderFlow.Application/Modules/Ordering/OrderTransitions.cs), Admin expected-version APIs, [real browser lifecycle](../client/e2e/admin.spec.ts)|Covered Confirmed→Processing→Shipped→Completed, no paid cancellation |
|A16|Append-only stock/history, immutable items/captures and business outbox rollback tests; restricted runtime grants|**Partial:** standalone stock integration outbox event obligation remains |
|A17–A18|[outbox fencing](../tests/OrderFlow.IntegrationTests/Messaging/OutboxPublishingTests.cs), [inbox dedup](../tests/OrderFlow.IntegrationTests/Notifications/NotificationTests.cs), payment/refund gates; direct reports under ADR-026|Local effects/fencing covered; no reporting projection exists. **Partial event contract:** confirmedAt/winning capture enrichment missing; do not claim versioned projection replay |
|A19|Finite delivery/reconciliation/SQL retry policies; exhausted work Failed/NeedsReview; email-only audited retry tests|**Partial operational scope:** no generic failure view/job/outbox/refund manual replay |
|A20|[frontend tests](../client/tests), [real browser flows](../client/e2e), exact cents/scoped keys/AbortSignal/session fences/persisted normalized intentions|Covered supported Customer/Admin UI, device-cart sharing explicitly documented |
|A21|Notification unique effects; [ReportingService](../src/OrderFlow.Application/Modules/Reporting/ReportingService.cs), [UTC/refund/snapshot/auth/plan tests](../tests/OrderFlow.IntegrationTests/Reporting)|Covered direct paid/captured/succeeded-refund/net semantics; projection replay N/A |
|A22–A23|[safe logs/OTel/readiness](../src/OrderFlow.Application/Observability), [tests](../tests/OrderFlow.IntegrationTests/Observability), real gateway transaction probes, optional dependencies excluded from checkout|Covered locally; SMTP can duplicate, buffer capacity finite, no production monitoring guarantee |
|A24|[README](../README.md), [three flow diagrams](flows.md), [seven-minute demo](demo.md), [interview guide](interview-guide.md), [CV map](cv.md), [remaining work](remaining-work.md)|Documentation/handoff delivered; it does not close the partial rows above |

Supplemental milestone acceptance also has direct evidence: cache detail hit/miss/mutation/outage/multi-instance tests ([Catalog](../tests/OrderFlow.IntegrationTests/Catalog)); Admin report boundaries/late refunds/money/auth/plans ([Reporting](../tests/OrderFlow.IntegrationTests/Reporting));32 registered browser flows for session/cart/checkout/payment/Admin/reconnect; optional OTel real transport trace lineage/readiness; two-replica deployment and two-worker gates; finite CI privacy/failure gates; bounded k6/fault scenarios with [recorded hardware/mix/tails/query/backlog/recovery and limits](load-testing.md). The load suite's exact OS-kill boundary remains unmeasured and is not called complete OS-crash evidence.

## Final execution record

Final check results are recorded here after fixes and in the current [implementation status](implementation-status.md). Historical passing counts elsewhere are historical observations, not a substitute for this audit's execution. Remote GitHub execution, public deployment, live payment/email and production-scale evidence remain unverified. Raw test logs/cookies/fixtures remain private; only structural counts and reproducible commands belong in public docs.


Executed locally after the material fixes (private raw roots retained):

| Check | Actual result |
| --- | --- |
|Locked restore / six-project Release build|Host SDK10.0.115, exit0,0 warnings/errors; final post-cleanup hosting build also exit0 |
|Unit gate|184passed/0failed/skipped |
|Final non-Browser/non-Deployment integration|459passed/0failed/skipped,308.731s runner window; actual PostgreSQL/RabbitMQ/Redis/Mailpit fixtures |
|Frontend gate|npm ci, configured Prettier, tsc/Vite and93 Vitest passed;0failed/skipped |
|Browser gate|32passed/0failed/skipped,142.465s runner window; real APIs/simulator/durable state, serialized case fixtures |
|Final hosting regressions|7passed/0failed/skipped,956ms, including repeat encrypted bootstrap/independent readers/disabled-config rejection |
|Real replicated profile|verify.sh exit0,1 deployment test plus built browser smoke; cross-node auth/AF, SQL checkout/worker arrival gates, SignalR and restart recovery |
|Fresh README demo|create/up/check succeeded; real built-UI rehearsal passed10 structural checks in26.278s automation (human narration is seven minutes), broker recovery7.80s; not a benchmark |
|Tool/report safety|3 demo +7 load +6 existing CI privacy tests passed; Python/Node/Bash syntax, actionlint and diff checks passed |
|Final image validation|All4 API/Worker/proxy/simulator images built after cleanup, exit0; no push or service startup in this final build step |

Exact commands: `ORDERFLOW_CI_ROOT=$PWD/.local/portfolio-final-checks-20261117 bash infra/ci/build-backend.sh`, then the same root for `test-backend.sh unit`, `test-frontend.sh` and `test-backend.sh browser`. The initial integration in that root reported458passed/1failed (telemetry ordering timeout). After fixing the setup, a fresh `ORDERFLOW_CI_ROOT=$PWD/.local/portfolio-integration-final-20261117` build and `bash infra/ci/test-backend.sh integration` reported459passed. No test retry setting, missing-dependency skip, larger wait or production algorithm change concealed that failure. A final separately compiled `FullyQualifiedName~DeploymentHostingTests` selection checked the nonsemantic duplicate-guard cleanup; unrelated suites were not repeated.

The first sandboxed local build exited before restore output; the approved NuGet/cache/network run completed. Initial demo rehearsal stopped at the new tool's missing snap compatibility entry, then that entry was added. A second rehearsal reached Confirmed but its Node APIRequestContext did not share Chromium's exact-certificate trust; the runner now uses browser fetch under the existing pin and catches raw credential-bearing exceptions. A diagnostic exposed a synthetic test cookie; no raw diagnostic was committed/published and subsequent runner output is structural only. The fresh corrected rehearsal completed on `.local/portfolio-rehearsal-20261117`. These initial failures remain distinct from final passes.

Replicated verification used `ORDERFLOW_VERIFY_SNAP_COMPAT=1 ORDERFLOW_VERIFY_PORT=18449 ORDERFLOW_VERIFY_PG_PORT=15439 ORDERFLOW_VERIFY_SUBNET=172.29.247.0/24 ORDERFLOW_VERIFY_PROXY_IP=172.29.247.10 bash infra/deployment/verify.sh`; test and smoke passed. New helper rehearsal used `ORDERFLOW_DEMO_DIRECTORY=$PWD/.local/portfolio-rehearsal-20261117 node infra/demo/rehearse.mjs`. Inputs/results under the three portfolio demo directories and `.local/deployment-verify-20261117084338` remain private. Final observation:0 containers for each owned project,5 durable volumes retained for each demo and4 for replica verification. No existing volume deletion, public target, real customer/payment/email, commit/push or remote workflow execution.

The earlier k6 benchmark was not rerun: only its startup now calls the explicit key tool; its published historical dataset/hardware/measurements/limits remain unchanged. Optional real telemetry-collector ingestion was not enabled in this audit; SDK/real-broker trace/readiness/redaction tests did run in the final integration gate. A green local run is not proof of GitHub-hosted provisioning, public deployment, production HA, key rotation/restore, sustained capacity or exact OS-kill-at-commit behavior. Unfulfilled stock/refund-failure events, enriched order envelopes and general operational replay remain listed in [remaining work](remaining-work.md).

Final static/privacy review validated27 changed/new UTF-8 files and468 relative Markdown links, structured JSON/fences/Python definitions/conflicts, exactly four CV bullets, actual final test summaries/hosting TRX and exclusion of generated credentials/cookies. Actionlint, Bash/Node syntax and git diff checks passed.
