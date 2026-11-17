#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
ci_root=${ORDERFLOW_CI_ROOT:-"$PWD/artifacts/ci"}
dotnet --version
dotnet restore OrderFlow.slnx --locked-mode --artifacts-path "$ci_root/build"
dotnet build OrderFlow.slnx -c Release --no-restore --artifacts-path "$ci_root/build" -m:1
