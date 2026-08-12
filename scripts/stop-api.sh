#!/usr/bin/env bash
# =============================================================================
# stop-api.sh — Precisely stop the Veda.Api process
#
# Only terminate Veda.Api, leaving other dotnet projects untouched.
# =============================================================================
set -euo pipefail

# Find and terminate all dotnet processes containing "Veda.Api"
if pkill -f "Veda.Api" 2>/dev/null; then
  echo "Veda.Api stopped."
else
  echo "No running Veda.Api process found."
fi
