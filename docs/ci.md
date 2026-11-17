# CI and local reproduction

[ADR-033](adr/0033-github-actions-and-browser-ci.md) defines the workflow and artifact policy. `.github/workflows/ci.yml` has four required-by-design jobs, with no deployment or image push. Configure branch protection separately to make their checks mandatory; writing a workflow does not configure GitHub repository settings.

## Clean-checkout prerequisites

- Linux with Docker daemon/Compose and permission to run disposable containers, plus registry access for the pinned PostgreSQL/RabbitMQ/Redis/Mailpit and Testcontainers cleanup images.
- Repository-pinned SDK10.0.115 for ordinary Ubuntu development, or the explicit published SDK10.0.404 CI/container pin in an isolated checkout. Supported .NET/ASP.NET runtime10.0.15; NuGet access for an empty cache.
- Node24.19.0, npm11.17.0, npm registry/browser-download access, Playwright's matching Chromium and Ubuntu browser libraries. Python3.12+ for safe report generation. Image-only job also uses OpenSSL.
- No live DB, payment gateway, real SMTP account, repository secret or cloud account. Unset ORDERFLOW_TEST_POSTGRES for CI helpers; they deliberately refuse inherited external DB connections.

GitHub-hosted ubuntu-24.04 provides Docker/Python/OpenSSL; pinned setup Actions install the exact tools and npx --no-install playwright install --with-deps chromium installs browser/library prerequisites. NuGet/npm caches contain packages only; no test DB/profile is reused. Action SHAs are verified stable releases and must be deliberately updated with their comments. The runner OS image receives normal updates.

For exact CI SDK reproduction, use a **separate temporary checkout**, copy infra/deployment/global.container.json to its global.json and install Microsoft SDK10.0.404. Check dotnet --version from that checkout. Do not overwrite the committed host global.json in your normal working directory. The workflow makes this copy only in its disposable workspace. The completed local verification used the exact SDK from the pinned MCR image and a fresh source copy/package directory; it did not change the developer SDK pin.

## Run the same gates locally

From the repository root with the appropriate installed SDK:

```bash
dotnet --version
node --version
npm --version
npm ci --prefix client
(cd client && npx --no-install playwright install --with-deps chromium)
python3 -m unittest discover -s infra/ci -p 'test_*.py'
bash infra/ci/build-backend.sh
bash infra/ci/test-backend.sh unit
bash infra/ci/test-backend.sh integration
bash infra/ci/test-frontend.sh
bash infra/ci/test-backend.sh browser
```

build-backend.sh performs locked restore and Release build for all six projects. It uses artifacts/ci/build consistently. Unit gate selects the UnitTests project only. Integration gate selects Category!=Browser&Category!=Deployment and runs actual dependency tests. Browser gate selects Category=Browser, currently32 cases, and requires the critical scenario set. Both integration/browser use Docker preflight and real fixture startup; missing dependencies are failures. Each fixture migrates only its generated DB. The opt-in replicated deployment rehearsal has its own [script/runbook](deployment.md) and is intentionally outside the default CI test selection.

test-frontend.sh runs only existing configured gates: Prettier, npm run build (tsc then Vite) and93 Vitest cases with JUnit output. No new .NET formatter/ESLint policy or broad reformat is imposed. Playwright is invoked by the .NET fixture harness with generated account/product/control values, not directly against an assumed shared server. Workers1/retries0 and isolated DB/catalog/account fixtures prevent cross-test inventory corruption. New .spec scenarios also need matching InlineData entries in the appropriate C# harness.

To choose another output location, set ORDERFLOW_CI_ROOT to an **absolute** private/ignored path before both build and test commands. It contains build/, raw/<suite>-<unique>/ and public/<suite>/. Raw output is private local debugging data. Do not upload it manually: it can contain assertion parameters, headers, browser error-context and fixture credentials.

## Failures and artifacts

The scripts preserve dotnet/Vitest's nonzero exit code. They additionally reject empty/skipped/malformed reports and missing required browser coverage. A test command that fails before producing TRX cannot pass by leaving an old success file: each run uses a fresh raw directory. The report helper removes its own stale public summary/images.

On job failure upload-artifact sends only artifacts/ci/public/, seven-day retention. JSON includes suite totals, static method/allowlisted scenario identity, source file/line and outcome; arbitrary assertion text, stdout, display parameters, environment, attachments and raw XML/HTML are omitted. Browser images come only from the explicit masked failure fixture, at most16 files/2MiB each. Inputs/textareas/data-sensitive elements are blacked out. Network trace/video/HAR/storage dumps and automatic raw images are excluded. Screenshot failure never overrides the original failing test. A runner killed before it can generate a report may have no artifact; that remains a failed/cancelled job, not passing acceptance.

Use the report's method/scenario and source line to reproduce locally. Inspect the private local runner.log/TRX/error-context when needed, without publishing their contents. To verify failure propagation safely, in a separate temporary copy change an assertion in InventoryDomainTests to an incorrect expected stock value, rebuild the UnitTests project with the same artifacts path and run the unit gate. It must return nonzero and public/unit/summary.json must contain valid=false/failed>0. Restore that temporary source afterward. Do not commit a deliberately failing invariant or weaken it to turn CI green.

## Build-only container validation

On a fresh checkout/private directory:

```bash
python3 infra/deployment/setup.py --directory .local/ci-images --project orderflow-ci-images
dc=(docker compose -f compose.deploy.yaml --env-file .local/ci-images/deployment.env --profile deploy --profile demo)
"${dc[@]}" config --quiet
"${dc[@]}" build api1 worker1 simulator proxy
```

Setup refuses overwriting existing secrets. Compose validates digest-pinned multi-stage API/Worker/simulator/frontend images, including locked restore and frontend typecheck/build. This does not start services, run migrations on a deployment DB, publish ports, push images or deploy. No production credential enters the build context. Existing volumes are unaffected.

Exact local results and remaining limits are in [implementation status](implementation-status.md). Workflow syntax/source checks and local commands have been executed; no workflow execution on GitHub is claimed until an authorized push/remote run exists. Remote runner networking, registry limits and branch-protection configuration remain unverified here.


The failure/load milestone additionally runs `python3 -m unittest discover -s infra/load -p 'test_*.py'` in the backend job for seven service-free target/fixture/report safety cases. Heavy k6/dependency-disruption scenarios are not started automatically in CI; they require the separate explicitly provisioned [disposable lab](load-testing.md). Existing integration selection already includes the underlying nine deterministic fault cases. No remote workflow execution is claimed for this addition.
