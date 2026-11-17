#!/usr/bin/env bash
set -euo pipefail
umask 077
cd "$(dirname "$0")/../.."
ci_root=${ORDERFLOW_CI_ROOT:-"$PWD/artifacts/ci"}
mkdir -p "$ci_root/raw" "$ci_root/public/frontend"
raw=$(mktemp -d "$ci_root/raw/frontend-XXXXXX")
# Prettier is already configured; no new ESLint/.NET formatting policy is imposed.
npm run format:check --prefix client
npm run build --prefix client # tsc -b then Vite: includes source, unit and e2e TypeScript.
set +e
npm test --prefix client -- --reporter=default --reporter=junit --outputFile="$raw/tests.xml" > "$raw/runner.log" 2>&1
test_status=$?
python3 infra/ci/report.py --format junit --suite frontend --raw "$raw" --public "$ci_root/public/frontend"
report_status=$?
set -e
if [[ $test_status != 0 ]]; then echo "Frontend tests failed (exit $test_status); raw diagnostics remain local at $raw." >&2; exit "$test_status"; fi
exit "$report_status"
