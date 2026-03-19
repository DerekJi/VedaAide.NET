#!/usr/bin/env bash
# =============================================================================
# smoke-test.sh — VedaAide.NET API 冒烟测试
#
# 用途：快速验证 API 核心流程（摄取 + 问答）是否正常工作。
# 使用：
#   chmod +x scripts/smoke-test.sh
#   ./scripts/smoke-test.sh [API_BASE_URL] [--start-api]
#
# 参数：
#   API_BASE_URL   可选，默认 http://localhost:5126
#   --start-api    可选，自动启动 Veda.Api（测试结束后自动停止）
#
# 注意：停止 API 时只终止 Veda.Api 进程，不影响其他 dotnet 项目。
# =============================================================================
set -euo pipefail

API_BASE="http://localhost:5126"
START_API=false

for arg in "$@"; do
  case $arg in
    --start-api) START_API=true ;;
    http*) API_BASE="$arg" ;;
  esac
done
PASS=0
FAIL=0

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# ── 可选：自动启动 API ─────────────────────────────────────────────────────────
if [[ "$START_API" == "true" ]]; then
  # 只停止 Veda.Api，不影响其他 dotnet 项目
  pkill -f "Veda.Api" 2>/dev/null && echo "Stopped previous Veda.Api." || true
  dotnet run --project "$REPO_ROOT/src/Veda.Api" &
  API_PID=$!
  echo "Waiting for Veda.Api (PID $API_PID)..."
  for i in $(seq 1 30); do
    curl -s -o /dev/null "$API_BASE/swagger/index.html" && break
    sleep 1
  done
  trap 'pkill -f "Veda.Api" 2>/dev/null || true' EXIT
fi

# ── Helpers ────────────────────────────────────────────────────────────────────
assert_contains() {
  local label="$1" body="$2" expected="$3"
  if echo "$body" | grep -qi "$expected"; then
    echo -e "${GREEN}[PASS]${NC} $label"
    ((PASS++)) || true
  else
    echo -e "${RED}[FAIL]${NC} $label"
    echo "       Expected response to contain: '$expected'"
    echo "       Got: $(echo "$body" | head -c 300)"
    ((FAIL++)) || true
  fi
}

assert_http_ok() {
  local label="$1" code="$2"
  if [[ "$code" == "200" ]]; then
    echo -e "${GREEN}[PASS]${NC} $label (HTTP $code)"
    ((PASS++)) || true
  else
    echo -e "${RED}[FAIL]${NC} $label (HTTP $code, expected 200)"
    ((FAIL++)) || true
  fi
}

# ── 0. API 健康检查 ──────────────────────────────────────────────────────────
echo -e "\n${YELLOW}=== VedaAide Smoke Test ===${NC}"
echo "Target: $API_BASE"
echo ""

echo "--- 0. Health check (Swagger) ---"
http_code=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/swagger/index.html")
assert_http_ok "Swagger UI accessible" "$http_code"

# ── 1. 文档摄取 ───────────────────────────────────────────────────────────────
echo ""
echo "--- 1. Document ingestion ---"
INGEST_BODY=$(curl -s -X POST "$API_BASE/api/documents" \
  -H "Content-Type: application/json" \
  -d '{
    "content": "VedaAide smoke test: The system follows SOLID principles. ISP stands for Interface Segregation Principle. DIP stands for Dependency Inversion Principle.",
    "documentName": "smoke-test-doc.txt",
    "documentType": "Specification"
  }')

INGEST_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_BASE/api/documents" \
  -H "Content-Type: application/json" \
  -d '{
    "content": "Duplicate ingestion test: this content should be deduplicated on second insert.",
    "documentName": "dedup-test.txt"
  }')

assert_http_ok "POST /api/documents returns 200" "200"
assert_contains "Response contains documentId"     "$INGEST_BODY" "documentId"
assert_contains "Response contains chunksStored"   "$INGEST_BODY" "chunksStored"
assert_contains "Response contains documentName"   "$INGEST_BODY" "documentName"

# ── 2. 输入验证 — 应返回 400 ─────────────────────────────────────────────────
echo ""
echo "--- 2. Input validation ---"
EMPTY_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_BASE/api/documents" \
  -H "Content-Type: application/json" \
  -d '{"content": "", "documentName": ""}')
if [[ "$EMPTY_CODE" == "400" ]]; then
  echo -e "${GREEN}[PASS]${NC} Empty content returns HTTP 400"
  ((PASS++)) || true
else
  echo -e "${RED}[FAIL]${NC} Empty content should return 400, got $EMPTY_CODE"
  ((FAIL++)) || true
fi

# ── 3. 问答查询 ───────────────────────────────────────────────────────────────
echo ""
echo "--- 3. Query (RAG pipeline) ---"
QUERY_BODY=$(curl -s -X POST "$API_BASE/api/query" \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What does ISP stand for in VedaAide?",
    "topK": 3,
    "minSimilarity": 0.4
  }')

QUERY_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_BASE/api/query" \
  -H "Content-Type: application/json" \
  -d '{"question": "What does ISP stand for in VedaAide?", "topK": 3, "minSimilarity": 0.4}')

assert_http_ok "POST /api/query returns 200" "$QUERY_CODE"
assert_contains "Response contains answer field"       "$QUERY_BODY" "answer"
assert_contains "Response contains sources field"      "$QUERY_BODY" "sources"
assert_contains "Response contains answerConfidence"   "$QUERY_BODY" "answerConfidence"

# ── 4. Query 输入验证 — 应返回 400 ───────────────────────────────────────────
echo ""
echo "--- 4. Query input validation ---"
QUERY_EMPTY_CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_BASE/api/query" \
  -H "Content-Type: application/json" \
  -d '{"question": ""}')
if [[ "$QUERY_EMPTY_CODE" == "400" ]]; then
  echo -e "${GREEN}[PASS]${NC} Empty question returns HTTP 400"
  ((PASS++)) || true
else
  echo -e "${RED}[FAIL]${NC} Empty question should return 400, got $QUERY_EMPTY_CODE"
  ((FAIL++)) || true
fi

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo -e "${YELLOW}=== Results ===${NC}"
TOTAL=$((PASS + FAIL))
echo "Passed: $PASS / $TOTAL"
if [[ $FAIL -gt 0 ]]; then
  echo -e "${RED}Failed: $FAIL${NC}"
  exit 1
else
  echo -e "${GREEN}All tests passed!${NC}"
  exit 0
fi
