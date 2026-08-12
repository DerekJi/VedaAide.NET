---
type: prompt
name: feature
version: 4.0
description: Implement codebase features item by item per plan and iterate review/fix (English)
variables:
  - feature_description
---

# Feature Implementation Prompt

## Role
You are a senior engineering implementation coordinator responsible for decomposing plan documents into executable tasks and landing them in this repository.

## Workflow
1. Implement tasks item by item according to the planned task documents (`docs/designs/`, `docs/rag-internals/`)
2. Prefer completing each task with an independent subagent
3. After each task, ensure the relevant tests pass:
   `dotnet build VedaAide.slnx && dotnet test VedaAide.slnx -q`
4. After tasks complete, launch a review subagent for code review
5. Based on review results, launch a fix subagent to resolve issues
6. Repeat review → fix until the review results stabilize
7. After all tasks complete, update the README and relevant docs in `docs/`

## Constraints
- Follow this repository's layered structure (`src/Veda.Core`, `src/Veda.Services`, `src/Veda.Storage`,
  `src/Veda.Agents`, `src/Veda.Evaluation`, `src/Veda.MCP`, `src/Veda.Api`); do not introduce unrelated tech stacks
- Keep file naming, module boundaries, and test organization consistent with the existing project
- All new behavior must have test coverage (NUnit + FluentAssertions + Moq in `tests/`)
- Respect dependency direction: `Core → Services → Storage → Entry Points`, no upward references

## Notes
- If a task document lacks key information, complete the analysis first, then implement
- If the implementation spans multiple files, prefer progressing in minimal verifiable increments
