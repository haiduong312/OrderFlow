#!/usr/bin/env bash
# Raw logs/TRX stay private; only an allowlisted structural report is published.
set -euo pipefail
umask 077
cd "$(dirname "$0")/../.."
ci_root=${ORDERFLOW_CI_ROOT:-"$PWD/artifacts/ci"}
suite=${1:?Choose unit, integration or browser}
case "$suite" in
  unit) project=tests/OrderFlow.UnitTests; filter='FullyQualifiedName~OrderFlow.UnitTests' ;;
  integration) project=tests/OrderFlow.IntegrationTests; filter='Category!=Browser&Category!=Deployment' ;;
  browser) project=tests/OrderFlow.IntegrationTests; filter='Category=Browser' ;;
  *) echo 'Choose unit, integration or browser.' >&2; exit 2 ;;
esac
mkdir -p "$ci_root/raw" "$ci_root/public/$suite"
raw=$(mktemp -d "$ci_root/raw/$suite-XXXXXX")
if [[ $suite != unit ]]; then
  # CI always uses disposable containers, never an accidentally inherited live connection.
  if [[ -n ${ORDERFLOW_TEST_POSTGRES:-} ]]; then
    echo 'Unset ORDERFLOW_TEST_POSTGRES: CI requires disposable Testcontainers.' >&2; exit 1
  fi
  if ! docker info > "$raw/docker.log" 2>&1; then
    printf '{"valid":false,"prerequisite":"Docker unavailable; tests not run"}\n' > "$ci_root/public/$suite/prerequisite.json"
    echo 'Docker prerequisite failed; integration/browser tests were not run.' >&2; exit 1
  fi
fi
if [[ $suite == browser ]]; then
  export ORDERFLOW_CI_BROWSER_ARTIFACTS="$raw/browser"
  export ORDERFLOW_SAFE_FAILURE_ARTIFACTS=1
fi
set +e
dotnet test "$project" -c Release --no-build --no-restore --artifacts-path "$ci_root/build" -m:1 \
  --filter "$filter" --logger 'trx;LogFileName=tests.trx' --results-directory "$raw" > "$raw/runner.log" 2>&1
test_status=$?
python3 infra/ci/report.py --format trx --suite "$suite" --raw "$raw" --public "$ci_root/public/$suite"
report_status=$?
set -e
if [[ $test_status != 0 ]]; then echo "$suite runner failed (exit $test_status); raw diagnostics remain local at $raw." >&2; exit "$test_status"; fi
exit "$report_status"
