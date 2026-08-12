#!/usr/bin/env bash
# =============================================================================
# smoke-test.sh — VedaAide.NET API smoke test
#
# Purpose: quickly verify that the API core flows (ingestion + Q&A) work correctly.
# Usage:
#   chmod +x scripts/smoke-test.sh
#   ./scripts/smoke-test.sh [API_BASE_URL] [--start-api]
#
# Arguments:
#   API_BASE_URL   Optional; defaults to http://localhost:5126
#   --start-api    Optional; automatically starts Veda.Api (stops it when the test finishes)
#
# Note: stopping the API only terminates the Veda.Api process and does not affect other dotnet projects.
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

# ── Optional: auto-start the API ─────────────────────────────────────────────
if [[ "$START_API" == "true" ]]; then
  # Stop only Veda.Api, leaving other dotnet projects untouched
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
# Writes the HTTP status code to CURL_CODE and the response body to CURL_BODY
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

# ── 0. API health check ──────────────────────────────────────────────────────────
echo -e "\n${YELLOW}=== VedaAide Smoke Test ===${NC}"
echo "Target: $API_BASE"
echo ""

echo "--- 0. Health check (Swagger) ---"
SWAGGER_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/swagger/index.html")
assert_http_code "Swagger UI accessible" "$SWAGGER_CODE" "200"

# ── 1. Document ingestion (Stage 1) ─────────────────────────────────────────────────────
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

# ── 2. Input validation — should return 400 ─────────────────────────────────────────────────
echo ""
echo "--- 2. Input validation ---"
curl_json POST "$API_BASE/api/documents" '{"content": "", "documentName": ""}'
assert_http_code "Empty content returns HTTP 400" "$CURL_CODE" "400"

# ── 3. Vector similarity dedup (Stage 2) ───────────────────────────────────────────────
echo ""
echo "--- 3. Similarity dedup (Phase 2) ---"
DEDUP_CONTENT='{"content": "VedaAide dedup probe: this exact sentence will be submitted twice to verify near-duplicate detection.", "documentName": "dedup-probe.txt"}'

# First ingestion
curl_json POST "$API_BASE/api/documents" "$DEDUP_CONTENT"
assert_http_code "First ingestion returns 201"      "$CURL_CODE" "201"
assert_contains  "First ingestion stores chunks"    "$CURL_BODY" "chunksStored"

# Second ingestion with identical content → similarity = 1.0, should trigger dedup → chunksStored = 0
curl_json POST "$API_BASE/api/documents" "$DEDUP_CONTENT"
assert_http_code "Second ingestion returns 201"                  "$CURL_CODE" "201"
assert_contains  "Duplicate ingestion: chunksStored is 0"        "$CURL_BODY" '"chunksStored":0'

# ── 4. Q&A query (Stage 1) ────────────────────────────────────────────────────
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

# ── 5. Hallucination guard field (Stage 2) ──────────────────────────────────────────────────
echo ""
echo "--- 5. Hallucination guard field (Phase 2) ---"
assert_contains "Response contains isHallucination field" "$CURL_BODY" "isHallucination"

# ── 6. Date range filter (Stage 2) ────────────────────────────────────────────────
echo ""
echo "--- 6. Date range filter (Phase 2) ---"
curl_json POST "$API_BASE/api/query" '{
  "question": "What does ISP stand for in VedaAide?",
  "dateFrom": "2099-01-01T00:00:00Z"
}'
assert_http_code "Query with future dateFrom returns 200"  "$CURL_CODE" "200"
assert_contains  "Future dateFrom yields no-info answer"  "$CURL_BODY" "don"
assert_not_contains "Future dateFrom returns no sources"  "$CURL_BODY" "documentName"

# ── 7. Query input validation — should return 400 ───────────────────────────────────────────
echo ""
echo "--- 7. Query input validation ---"
curl_json POST "$API_BASE/api/query" '{"question": ""}'
assert_http_code "Empty question returns HTTP 400" "$CURL_CODE" "400"

# ── 8. Structured output (Stage 3 Sprint 3) ────────────────────────────────────────────
echo ""
echo "--- 8. Structured output (Stage 3 Sprint 3) ---"
curl_json POST "$API_BASE/api/query" '{
  "question": "What are the SOLID principles in VedaAide?",
  "structuredOutput": true,
  "topK": 3,
  "minSimilarity": 0.3
}'
assert_http_code "Structured output query returns 200"   "$CURL_CODE" "200"
assert_contains  "Response contains answer field"        "$CURL_BODY" "answer"

# ── 9. Document version history (Stage 3 Sprint 3) ──────────────────────────────────────────
echo ""
echo "--- 9. Document version history (Stage 3 Sprint 3) ---"
CURL_CODE=$(curl -s -o "$TMPFILE" -w "%{http_code}" "$API_BASE/api/admin/documents/smoke-test-doc.txt/history" \
  -H "Content-Type: application/json")
CURL_BODY=$(cat "$TMPFILE")
assert_http_code "GET /api/admin/documents/{name}/history returns 200" "$CURL_CODE" "200"
assert_contains  "History response contains documentName"              "$CURL_BODY" "documentName"

# ── 10. Feedback recording (Stage 3 Sprint 4) ─────────────────────────────────────────────
echo ""
echo "--- 10. Feedback recording (Stage 3 Sprint 4) ---"
curl_json POST "$API_BASE/api/feedback" '{
  "userId": "smoke-test-user",
  "sessionId": "smoke-session-1",
  "type": "ResultAccepted",
  "relatedChunkId": "",
  "relatedDocumentId": "",
  "query": "What does ISP stand for?"
}'
assert_http_code "POST /api/feedback returns 202"   "$CURL_CODE" "202"

# ── 11. Feedback stats (Stage 3 Sprint 4) ─────────────────────────────────────────────
echo ""
echo "--- 11. Feedback stats (Stage 3 Sprint 4) ---"
CURL_CODE=$(curl -s -o "$TMPFILE" -w "%{http_code}" "$API_BASE/api/feedback/stats?userId=smoke-test-user")
CURL_BODY=$(cat "$TMPFILE")
assert_http_code "GET /api/feedback/stats returns 200"   "$CURL_CODE" "200"
assert_contains  "Stats response contains totalEvents"   "$CURL_BODY" "totalEvents"

# ── 12. Knowledge governance — visibility check (Stage 3 Sprint 4) ────────────────────────────────
echo ""
echo "--- 12. Governance visibility check (Stage 3 Sprint 4) ---"
CURL_CODE=$(curl -s -o "$TMPFILE" -w "%{http_code}" "$API_BASE/api/governance/documents/nonexistent-doc/visible?userId=smoke-test-user")
CURL_BODY=$(cat "$TMPFILE")
assert_http_code "GET governance visibility returns 200"  "$CURL_CODE" "200"
assert_contains  "Visibility response contains visible field" "$CURL_BODY" "visible"

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
