#!/usr/bin/env bash
# =============================================================================
# stop-api.sh — 精确停止 Veda.Api 进程
#
# 只终止 Veda.Api，不影响其他 dotnet 项目。
# =============================================================================
set -euo pipefail

# 找到所有包含 "Veda.Api" 的 dotnet 进程并终止
if pkill -f "Veda.Api" 2>/dev/null; then
  echo "Veda.Api stopped."
else
  echo "No running Veda.Api process found."
fi
