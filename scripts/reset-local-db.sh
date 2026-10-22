#!/usr/bin/env bash
# ⚠️ XOÁ dữ liệu Docker Compose local (named volumes) rồi khởi động lại. CHỈ DÀNH CHO DEV.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "${ASPNETCORE_ENVIRONMENT:-Development}" != "Development" ]]; then
  echo "Từ chối: ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT}" >&2; exit 1
fi
echo "CẢNH BÁO: sẽ xoá volumes sqldata, redisdata, rabbitdata."
if [[ "${1:-}" != "--force" ]]; then
  read -r -p "Gõ RESET để tiếp tục: " answer
  [[ "$answer" == "RESET" ]] || { echo "Đã huỷ."; exit 1; }
fi
docker compose down -v
docker compose up -d --build
echo "Seed dữ liệu demo: docker compose run --rm migrator --migrate-only --seed-only"
