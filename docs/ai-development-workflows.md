# AI Development Workflows (GitHub Actions)

This repository ships three GitHub Actions workflows that let an AI agent
([Reasonix](https://www.npmjs.com/package/reasonix) + DeepSeek) develop, review,
and fix code automatically. They are adapted from the reference setup used in
the `diffusion-rag` project.

## Workflows

| Workflow | Trigger | What it does |
|----------|---------|--------------|
| `reasonix_develop.yml` | An Issue gets the `ai-dev` label | Runs Reasonix autonomously on the Issue requirements, then opens a PR (`reasonix-dev/issue-<n>`) that closes the Issue |
| `reasonix_pr_feedback.yml` | A comment containing `/fix` is posted on a PR | Checks out the PR branch, runs Reasonix to address the review feedback, verifies, and pushes the fix |
| `reasonix_pr_pipeline.yml` | PR opened / updated / review submitted | Runs static checks (`dotnet build` / `test`), auto-fixes failures with AI, runs an AI code review, auto-fixes review findings, pushes once everything is green |

All three skip bot-authored commits to avoid infinite loops, and the pipeline
workflow uses a per-PR concurrency group.

## Required Secrets

Configure these in **Settings → Secrets and variables → Actions**:

| Secret | Purpose |
|--------|---------|
| `DEEPSEEK_API_KEY` | API key for the Reasonix model (DeepSeek) |
| `GH_PAT` | A Personal Access Token with `repo` scope, used by `create-pull-request` and pushes (must be a PAT, not `GITHUB_TOKEN`, so the PR creation can trigger other workflows) |

## Configuring a Task

The `ai-dev` Issue (and the auto-created PR body) can carry optional yaml-style
fields that tune the run:

```text
model: deepseek-flash    # deepseek-flash (default) | deepseek-pro
level: medium            # low | medium (default) | high
type: code               # code (default) | docs
```

- `model` picks the LLM used by Reasonix (`deepseek-pro` for harder tasks).
- `level` sets the effort level passed to the agent.
- `type: docs` switches the task prompt to documentation standards instead of code.

## Review Feedback on a PR

To ask the agent to fix review comments, reply on the PR:

```text
/fix <what you want changed>
```

The agent modifies the PR branch, runs `dotnet build` / `dotnet test`,
`dotnet test`, and pushes the result (with one self-repair round if checks fail).

## Notes

- All generated prompts live under `$RUNNER_TEMP` so they never pollute the diff.
- Project engineering standards injected into the agent prompts live in
  `.reasonix/commands/` (`develop.md`, `code-review.md`, `fix-bug.md`, `feature.md`,
  `e2e-test.md`, `github-issues.md`) plus `.reasonix/copilot-instructions.md` —
  update them to change what the AI is told about this codebase.
- `reasonix.toml` configures the local Reasonix CLI (models, permissions, sandbox).
