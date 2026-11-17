# ADR-033: GitHub Actions gates and private failure artifacts

Status: accepted, 2026-11-17. Scope: requested CI/browser acceptance; no push or deployment.

## Toolchain and jobs

Use GitHub-hosted ubuntu-24.04, exact stable Action commit SHAs with release comments, read-only contents permission and checkout persist-credentials=false. Run on push/pull_request/workflow_dispatch, never privileged pull_request_target. Cancel superseded runs by ref; different jobs/runs own separate disposable dependencies. No repository secret, payment-provider credential, registry login or real email delivery is required.

The root SDK10.0.115 is Ubuntu-packaged. ADR-032 already pins published Microsoft SDK10.0.404 for containers. Extend that deliberate pin to CI: copy infra/deployment/global.container.json over global.json **only in the disposable Actions checkout**, then setup-dotnet installs that exact version. Do not change the committed development pin or silently roll forward. Locked NuGet restores and existing runtime/package versions remain. Node24.19.0/npm11.17.0/npm ci match client tooling; Playwright1.65.0 installs its own matching Chromium with Ubuntu dependencies. Runner OS patches can move; this is not a byte-for-byte OS snapshot.

Four jobs separate backend, frontend, browser and image validation. Backend builds the solution, tests the service-free UnitTests project and all non-Browser/non-Deployment integration tests. Browser builds independently and runs every registered real browser case. Frontend uses the existing Prettier configuration, tsc/Vite build and Vitest; do not add ESLint, .NET formatting policy or unrelated source rewrites. Image job builds all custom multi-stage targets using existing pins and privately generated inputs. It never starts the application, pushes images or deploys.

## Real dependencies and browser coverage

Reuse actual Testcontainers PostgreSQL/RabbitMQ/Redis/Mailpit fixtures with existing image digests, bounded startup and cleanup, random credentials/ports and a new migrated DB per case. Reject inherited ORDERFLOW_TEST_POSTGRES in CI helpers; a missing Docker prerequisite fails explicitly. No EF InMemory, SQLite, fake successful API response, live payment or external SMTP substitution. Schema migrations belong to the test fixture's owned DB, not a developer/production DB.

Reuse the31 meaningful Playwright flows under the .NET harness and add flow_registered_purchase: register through UI, sign in as that newly created Customer, browse/add exact cart, create/reserve, initiate simulator payment, reconcile capture and receive authoritative status. Existing cases separately verify lost-response same-key replay, expired/late payments/refunds, Admin fulfillment/reports, ownership/account switching and actual socket reconnect/durable catch-up. The harness's test-only nonce controls advance logical time and drive production services; production hosts never map them. No payment/control key reaches browser app code.

xUnit DB collections disable parallelization and each case has its own DB/accounts/catalog/stock; Playwright workers=1, fullyParallel=false, retries=0. CI jobs have separate daemons/resources. Do not run the .spec files against a shared persistent shop or add unregistered browser cases: register each scenario with the appropriate .NET InlineData harness. Fixed business timestamps/prices/quantities and real response/state gates determine assertions; random IDs only isolate resources.

The exact test shell command exit code remains authoritative. Report validation also rejects zero tests, any skip/failure and missing required browser scenarios/minimum32 cases. The opt-in deployment rehearsal is explicitly excluded, not counted as passing. Backend integration still covers actual broker/SMTP/Redis; browser tests use actual HTTP/SQL/simulator/SignalR and deterministic production-service drivers rather than background polling races.

## Failure artifact policy

Test stdout/TRX/JUnit/error-context remain in ignored raw directories and are never uploaded or replayed to Actions console. Assertion messages/display parameters can contain generated credentials or header values; use an allowlist instead of claiming regex can identify every secret. Publish only method/allowlisted scenario/source file/line/outcome/count metadata from structural XML. Missing/malformed/stale reports cannot turn a failed command green. Preserve useful failed locations and local reproduction commands; raw details can be inspected locally.

Keep Playwright trace/video/HAR/storage capture off. A per-test automatic fixture depends on page, captures unexpected failures before page teardown and masks inputs/textareas/data-sensitive elements. Copy only explicitly named masked PNGs with size/count bounds; exclude all arbitrary attachments, automatic images, HTML/error-context and raw ZIPs. These are generated .invalid fixture accounts, not production users. Uploaded artifacts contain only sanitized summaries and these masked images, retained7 days. No cache contains DB volumes/cookies/browser profiles; package caches use committed lockfiles.

## Verification

The same shell scripts are local entry points. A deliberately wrong stock-invariant assertion in an isolated temporary checkout returned exit1 through the actual unit gate; restored source passed again. Safe-report tests cover failed/empty/skipped/missing-browser cases and removal of synthetic secret values. A separate intentionally failed synthetic browser page verified input pixels were masked; no fake API was involved. See [CI reproduction](../ci.md) and [status](../implementation-status.md) for exact executed checks. Local matching-SDK execution and workflow syntax validation do not prove a GitHub-hosted workflow ran. Branch protection/required checks and a first remote run require repository configuration/push authorization outside this task.

Sources: [setup-dotnet](https://github.com/actions/setup-dotnet), [setup-node](https://github.com/actions/setup-node), [checkout](https://github.com/actions/checkout), [upload-artifact privacy/retention](https://github.com/actions/upload-artifact), [Playwright CI](https://playwright.dev/docs/ci), [Playwright fixture teardown](https://playwright.dev/docs/test-fixtures), [Testcontainers .NET](https://dotnet.testcontainers.org/).
