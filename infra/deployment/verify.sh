#!/usr/bin/env bash
# Owns a NEW isolated project only. Leaves volumes intact, including after failure.
set -euo pipefail
cd "$(dirname "$0")/../.."
stamp=$(date -u +%Y%m%d%H%M%S)
verification_directory="$PWD/.local/deployment-verify-$stamp"
python3 infra/deployment/setup.py --directory "$verification_directory" --project "orderflow-verify-$stamp" \
  --port "${ORDERFLOW_VERIFY_PORT:-18443}" --subnet "${ORDERFLOW_VERIFY_SUBNET:-172.29.241.0/24}" --proxy-ip "${ORDERFLOW_VERIFY_PROXY_IP:-172.29.241.10}"
compose=(docker compose -f compose.deploy.yaml -f infra/deployment/compose.verify.yaml)
if [[ ${ORDERFLOW_VERIFY_SNAP_COMPAT:-0} == 1 ]]; then compose+=(-f infra/deployment/compose.snap-compat.yaml); fi
compose+=(--env-file "$verification_directory/deployment.env" --profile deploy)
cleanup() {
  if [[ ${ORDERFLOW_VERIFY_KEEP_RUNNING:-0} != 1 ]]; then "${compose[@]}" down --timeout 30; fi
  printf 'Verification inputs and volumes retained: %s\n' "$verification_directory"
}
trap cleanup EXIT
"${compose[@]}" config --quiet
"${compose[@]}" build api1 worker1 proxy
"${compose[@]}" up -d --wait --wait-timeout 120 postgres rabbitmq redis
"${compose[@]}" run --rm migrate
"${compose[@]}" run --rm bootstrap-admin
"${compose[@]}" run --rm initialize-keys
"${compose[@]}" up -d --wait --wait-timeout 150 api1 api2 worker1 worker2 proxy
ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG="$verification_directory" dotnet test tests/OrderFlow.IntegrationTests \
  -c Release --filter 'FullyQualifiedName~DeploymentProfileTests' --logger 'console;verbosity=normal'
ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG="$verification_directory" node infra/deployment/browser-smoke.mjs
