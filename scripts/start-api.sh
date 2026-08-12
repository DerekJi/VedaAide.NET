#!/usr/bin/env bash
# =============================================================================
# start-api.sh — Start the Veda.Api development server
#
# Purpose: start the API in the background and return once it is ready.
# Usage:
#   chmod +x scripts/start-api.sh
#   ./scripts/start-api.sh [PORT]
#
# Arguments:
#   PORT  Optional; defaults to the launchSettings configuration (usually 5126)
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API_PROJECT="$REPO_ROOT/src/Veda.Api"
PORT="${1:-5126}"
MAX_WAIT=30

# Stop the old Veda.Api instance (if running), without affecting other projects
pkill -f "Veda.Api" 2>/dev/null && echo "Stopped previous Veda.Api." || true

echo "Starting Veda.Api on port $PORT..."
dotnet run --project "$API_PROJECT" &
API_PID=$!
echo "PID: $API_PID"

# Wait for the API to become ready
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
