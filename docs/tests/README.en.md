# Tests Documentation: Overview

This directory contains test strategy designs, test methodology notes, and per-phase test guides.

> 中文版见 [README.cn.md](README.cn.md)

## Directory Structure

```
docs/tests/
├── README.cn.md / README.en.md                       ← This file — test system overview
├── phase1-rag-tests.cn.md / .en.md                   ← Phase 1: core RAG engine tests
├── phase2-rag-tests.cn.md / .en.md                   ← Phase 2: RAG quality tests (dedup, anti-hallucination, reranking, date filter)
├── phase4-agent-mcp-tests.cn.md / .en.md             ← Phase 4/5: agent orchestration, MCP tools, prompt module, external data sources
└── test-conventions.cn.md / .en.md                   ← Test naming conventions and coding standards
```

## Test Layers

| Layer | Purpose | Tools | How to run |
|---|---|---|---|
| **Unit tests** | Verify business logic of individual classes/methods, no external services | NUnit + Moq | `dotnet test` |
| **Integration tests** | Verify multi-module collaboration (EF Core + SQLite), using in-memory/temp DB | NUnit | `dotnet test` |
| **Smoke tests** | Verify main API end-to-end flows are functional | Bash (`scripts/smoke-test.sh`) | `./scripts/smoke-test.sh` |
| **AI evaluation tests** | Verify RAG output quality (faithfulness, relevancy, etc.) | `Veda.Evaluation` (Phase 6, pending) | `dotnet test` |

## Quick Run

```bash
# Unit + integration tests
dotnet test

# Smoke tests (start the API first, then run)
dotnet run --project src/Veda.Api &
sleep 8
./scripts/smoke-test.sh

# Smoke tests (auto start/stop API, all in one command)
./scripts/smoke-test.sh --start-api

# Custom API address
./scripts/smoke-test.sh http://your-server:5126
```

## Start / Stop API (Development)

```bash
# Start (waits until ready, then returns)
./scripts/start-api.sh

# Stop (only stops Veda.Api, does not affect other dotnet projects)
./scripts/stop-api.sh
```

> **Warning:** Never use `taskkill //F //IM dotnet.exe` or `pkill dotnet` to release file locks —
> this kills **all** dotnet processes. Always use `pkill -f "Veda.Api"` to target only this project.

## Coverage Requirements

- `Veda.Core`: ≥ 80% (pure domain logic, should be high)
- `Veda.Services`: ≥ 80% (mock LLM + mock VectorStore)
- `Veda.Storage`: ≥ 60% (covered by integration tests)
- `Veda.Api`: smoke tests covering main endpoints is sufficient
