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
TMPFILE=$(mktemp)
trap 'rm -f "$TMPFILE"; pkill -f "Veda.Api" 2>/dev/null || true' EXIT

# ── 可选：自动启动 API ─────────────────────────────────────────────────────────
if [[ "$START_API" == "true" ]]; then
  # 只停止 Veda.Api，不影响其他 dotnet 项目
  pkill -f "Veda.Api" 2>/dev/null && echo "Stopped previous Veda.Api." || true
  dotnet run --project "$REPO_ROOT/src/Veda.Api" &
  echo "Waiting for Veda.Api to start..."
  for i in $(seq 1 30); do
    curl -s -o /dev/null "$API_BASE/swagger/index.html" && break
    sleep 1
  done
fi

# ── Helpers ────────────────────────────────────────────────────────────────────

# curl_json <method> <url> <json_body>
# 将 HTTP 状态码写入 CURL_CODE，响应体写入 CURL_BODY
curl_json() {
  local method="$1" url="$2" body="$3"
  CURL_CODE=$(curl -s -o "$TMPFILE" -w "%{http_code}" -X "$method" "$url" \
    -H "Content-Type: application/json" -d "$body")
  CURL_BODY=$(cat "$TMPFILE")
}

assert_http_code() {
  local label="$1" code="$2" expected="$3"
  if [[ "$code" == "$expected" ]]; then
    echo -e "${GREEN}[PASS]${NC} $label (HTTP $code)"
    ((PASS++)) || true
  else
    echo -e "${RED}[FAIL]${NC} $label (HTTP $code, expected $expected)"
    ((FAIL++)) || true
  fi
}

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

assert_not_contains() {
  local label="$1" body="$2" unexpected="$3"
  if echo "$body" | grep -qi "$unexpected"; then
    echo -e "${RED}[FAIL]${NC} $label"
    echo "       Expected response NOT to contain: '$unexpected'"
    echo "       Got: $(echo "$body" | head -c 300)"
    ((FAIL++)) || true
  else
    echo -e "${GREEN}[PASS]${NC} $label"
    ((PASS++)) || true
  fi
}

# ── 0. API 健康检查 ──────────────────────────────────────────────────────────
echo -e "\n${YELLOW}=== VedaAide Smoke Test ===${NC}"
echo "Target: $API_BASE"
echo ""

echo "--- 0. Health check (Swagger) ---"
SWAGGER_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/swagger/index.html")
assert_http_code "Swagger UI accessible" "$SWAGGER_CODE" "200"

# ── 1. 文档摄取（阶段一） ─────────────────────────────────────────────────────
echo ""
echo "--- 1. Document ingestion ---"
curl_json POST "$API_BASE/api/documents" '{
  "content": "VedaAide smoke test: The system follows SOLID principles. ISP stands for Interface Segregation Principle. DIP stands for Dependency Inversion Principle.",
  "documentName": "smoke-test-doc.txt",
  "documentType": "Specification"
}'
assert_http_code "POST /api/documents returns 201"  "$CURL_CODE" "201"
assert_contains  "Response contains documentId"     "$CURL_BODY" "documentId"
assert_contains  "Response contains chunksStored"   "$CURL_BODY" "chunksStored"
assert_contains  "Response contains documentName"   "$CURL_BODY" "documentName"

# ── 2. 输入验证 — 应返回 400 ─────────────────────────────────────────────────
echo ""
echo "--- 2. Input validation ---"
curl_json POST "$API_BASE/api/documents" '{"content": "", "documentName": ""}'
assert_http_code "Empty content returns HTTP 400" "$CURL_CODE" "400"

# ── 3. 向量相似度去重（阶段二） ───────────────────────────────────────────────
echo ""
echo "--- 3. Similarity dedup (Phase 2) ---"
DEDUP_CONTENT='{"content": "VedaAide dedup probe: this exact sentence will be submitted twice to verify near-duplicate detection.", "documentName": "dedup-probe.txt"}'

# 第一次摄取
curl_json POST "$API_BASE/api/documents" "$DEDUP_CONTENT"
assert_http_code "First ingestion returns 201"      "$CURL_CODE" "201"
assert_contains  "First ingestion stores chunks"    "$CURL_BODY" "chunksStored"

# 第二次摄取完全相同的内容 → 相似度 = 1.0，应触发去重 → chunksStored = 0
curl_json POST "$API_BASE/api/documents" "$DEDUP_CONTENT"
assert_http_code "Second ingestion returns 201"                  "$CURL_CODE" "201"
assert_contains  "Duplicate ingestion: chunksStored is 0"        "$CURL_BODY" '"chunksStored":0'

# ── 4. 问答查询（阶段一） ────────────────────────────────────────────────────
echo ""
echo "--- 4. Query (RAG pipeline) ---"
curl_json POST "$API_BASE/api/query" '{
  "question": "What does ISP stand for in VedaAide?",
  "topK": 3,
  "minSimilarity": 0.4
}'
assert_http_code "POST /api/query returns 200"             "$CURL_CODE" "200"
assert_contains  "Response contains answer field"          "$CURL_BODY" "answer"
assert_contains  "Response contains sources field"         "$CURL_BODY" "sources"
assert_contains  "Response contains answerConfidence"      "$CURL_BODY" "answerConfidence"

# ── 5. 防幻觉字段（阶段二） ──────────────────────────────────────────────────
echo ""
echo "--- 5. Hallucination guard field (Phase 2) ---"
assert_contains "Response contains isHallucination field" "$CURL_BODY" "isHallucination"

# ── 6. 日期范围过滤（阶段二） ────────────────────────────────────────────────
echo ""
echo "--- 6. Date range filter (Phase 2) ---"
curl_json POST "$API_BASE/api/query" '{
  "question": "What does ISP stand for in VedaAide?",
  "dateFrom": "2099-01-01T00:00:00Z"
}'
assert_http_code "Query with future dateFrom returns 200"  "$CURL_CODE" "200"
assert_contains  "Future dateFrom yields no-info answer"  "$CURL_BODY" "don"
assert_not_contains "Future dateFrom returns no sources"  "$CURL_BODY" "documentName"

# ── 7. Query 输入验证 — 应返回 400 ───────────────────────────────────────────
echo ""
echo "--- 7. Query input validation ---"
curl_json POST "$API_BASE/api/query" '{"question": ""}'
assert_http_code "Empty question returns HTTP 400" "$CURL_CODE" "400"

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
