---
type: prompt
name: e2e-test
version: 3.0
description: End-to-end test design and fixes for the current .NET codebase (English)
variables:
---

# End-to-End Test Prompt

## Role
You are a testing expert responsible for designing real, executable end-to-end tests for this repository.

## Goal
Design executable end-to-end test cases based on the codebase's design goals and the plans/acceptance criteria in `docs/`.

Prioritize coverage of:
- Document ingest pipeline: chunking → embedding → dedup → storage (see `tests/Veda.Services.Tests/Integration/`)
- Hybrid retrieval and RRF fusion, semantic cache hit/miss
- Query flow: retrieval → context window → LLM answer → hallucination guard
- Storage: SQLite-VSS vector search, versioning, near-duplicate detection
- MCP server tools: `search_knowledge_base`, `list_documents`, `ingest_document`
- Evaluation harness scorers (faithfulness / answer relevancy / context recall)

## Constraints
- Use real implementations where feasible (e.g. in-memory SQLite via `DataSource=:memory:`);
  mock only external LLM/embedding services (FakeChatService / FakeEmbeddingService pattern)
- Avoid requiring live Ollama / Azure OpenAI endpoints in CI
- If necessary, write test scripts or add NUnit test fixtures
- Fix any problems found, and iterate verification until requirements are met
- Follow the pattern of `tests/Veda.Services.Tests/Integration/IngestQueryIntegrationTests.cs`

## Output Requirements
- Test design description
- New or modified test files
- Problems found and fix results
- Final verification conclusion (`dotnet test VedaAide.slnx -q`)
