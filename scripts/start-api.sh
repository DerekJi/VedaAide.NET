#!/usr/bin/env bash
# =============================================================================
# start-api.sh — 启动 Veda.Api 开发服务器
#
# 用途：后台启动 API，并等待其就绪后返回。
# 使用：
#   chmod +x scripts/start-api.sh
#   ./scripts/start-api.sh [PORT]
#
# 参数：
#   PORT  可选，默认读取 launchSettings 配置（通常 5126）
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API_PROJECT="$REPO_ROOT/src/Veda.Api"
PORT="${1:-5126}"
MAX_WAIT=30

# 停掉旧的 Veda.Api（如果在跑），不影响其他项目
pkill -f "Veda.Api" 2>/dev/null && echo "Stopped previous Veda.Api." || true

echo "Starting Veda.Api on port $PORT..."
dotnet run --project "$API_PROJECT" &
API_PID=$!
echo "PID: $API_PID"

# 等待 API 就绪
elapsed=0
until curl -s -o /dev/null "http://localhost:$PORT/swagger/index.html"; do
  sleep 1
  ((elapsed++)) || true
  if [[ $elapsed -ge $MAX_WAIT ]]; then
    echo "ERROR: Veda.Api did not start within ${MAX_WAIT}s" >&2
    kill $API_PID 2>/dev/null || true
    exit 1
  fi
done

echo "Veda.Api is ready at http://localhost:$PORT"
