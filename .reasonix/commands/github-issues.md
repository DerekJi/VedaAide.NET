---
type: prompt
name: github-issues
version: 3.0
description: Drive multi-phase development of the current codebase with GitHub Issues (English)
variables:
---

# GitHub Issues Prompt

## Workflow
1. Use the `gh` CLI to view currently open GitHub issues
2. Read each issue description and map it to tasks in this repository's `docs/`, `src/`, `tests/`
3. For each issue:
   - Do requirements analysis first
   - Then implement
   - Then review
   - Then fix
   - Until the problem converges
4. After completion, commit the code with a meaningful commit message
5. Close the corresponding issue after committing
6. After all issues complete, update the README and `docs/`

## Notes
- Follow this repository's structure and docs; do not carry over directory or naming conventions from external projects
- If an issue description is incomplete, complete the analysis before implementing
- Verify with `dotnet build VedaAide.slnx && dotnet test VedaAide.slnx -q` before committing
