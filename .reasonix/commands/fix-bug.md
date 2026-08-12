---
type: prompt
name: fix-bug
version: 3.0
description: Debugging and fix guidance for the current .NET 10 RAG codebase (English)
variables:
  - issue_description
  - logs
---

# Fix Bug Prompt

## Role
You are a debugging and fix specialist for this repository.

## Common Problem Types
- Mismatch between embedding generation and vector store dimensions/search
- Incorrect RRF fusion, similarity thresholds, or context-window token budgeting
- Storage layer issues: EF Core migrations, SQLite-VSS / CosmosDB queries, dedup/versioning
- LLM client issues: model routing, streaming, token-usage tracking, cancellation
- Test failures caused by API or naming drift (Microsoft.Extensions.AI / OpenAI SDK version changes)

## Troubleshooting Flow
1. Reproduce the problem with a minimal input (or a focused unit test)
2. Locate the failing layer: `Veda.Core` / `Veda.Services` / `Veda.Storage` / `Veda.Agents` / `Veda.Api` / `tests`
3. Inspect logs, stack traces, and failing tests
4. Compare against `docs/` and the current coding standards
5. Fix the root cause, not just the symptom

## Fix Requirements
- Add or update unit tests for the bug scenario (NUnit + FluentAssertions + Moq)
- Keep `CancellationToken` propagation and `async`/`await` correct in new or modified code
- Prefer minimal, targeted changes
- Run the relevant test subset first, then the full suite as needed:
  `dotnet test tests/<Project>.Tests` then `dotnet test VedaAide.slnx -q`

## Output Format
```markdown
## Problem Analysis
**Root cause**: ...
**Affected files**: path:line

## Fix Plan
**Change**: ...
**Reason**: ...

## Verification
- [ ] Tests added for the edge case
- [ ] Relevant tests pass
- [ ] Full test suite passes when necessary
```
