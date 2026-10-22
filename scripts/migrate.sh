#!/usr/bin/env bash
# Áp dụng migrations (non-destructive) và tuỳ chọn seed dữ liệu DEVELOPMENT.
#   ./scripts/migrate.sh          # migrate
#   ./scripts/migrate.sh --seed   # migrate + seed demo data (dev-only)
set -euo pipefail
cd "$(dirname "$0")/../source/backend"
args=(--migrate-only)
[[ "${1:-}" == "--seed" ]] && args+=(--seed-only)
dotnet run --project src/Fnb.Api --launch-profile http -- "${args[@]}"
echo "Migration hoàn tất."
