# Phase 6: AI Evaluation System — Test Plan

> English version · 中文版: [phase6-eval-tests.cn.md](phase6-eval-tests.cn.md)

## Overview

This document describes the testing strategy, coverage scope, and individual test class descriptions for Phase 6 (AI Evaluation System).

**Test project**: `tests/Veda.Evaluation.Tests/`
**Module under test**: `src/Veda.Evaluation/` (Scorers + EvaluationRunner)

---

## Testing Strategy

- **All mocked**: `IChatService` (LLM) and `IEmbeddingService` are substituted via Moq — zero external dependencies, very fast.
- **Coverage paths**: happy paths + edge cases (empty lists, max/zero scores) + error/resilience paths.
- **Coverage target**: `Veda.Evaluation` ≥ 90% (actual: 94.5% line / 100% branch).

---

## Test Classes

### `FaithfulnessScorerTests` (6 tests)

Tests `FaithfulnessScorer.ScoreAsync()` — LLM faithfulness scoring.

| Test name | Scenario | Expected |
|---|---|---|
| `ScoreAsync_LlmReturnsOne_ShouldReturnOnePointZero` | LLM returns "1.0" | 1.0f |
| `ScoreAsync_LlmReturnsZero_ShouldReturnZero` | LLM returns "0.0" | 0.0f |
| `ScoreAsync_LlmReturnsPartialScore_ShouldClampToRange` | LLM returns "0.65" | ≈0.65f |
| `ScoreAsync_LlmReturnsOutOfRangeHigh_ShouldClampToOne` | LLM returns "1.5" (out of range) | 1.0f (clamped) |
| `ScoreAsync_LlmReturnsGarbage_ShouldReturnZero` | LLM returns non-numeric string | 0.0f (fallback) |
| `ScoreAsync_LlmThrows_ShouldReturnZero` | LLM throws exception | 0.0f (resilient) |

---

### `AnswerRelevancyScorerTests` (3 tests)

Tests `AnswerRelevancyScorer.ScoreAsync()` — cosine similarity between question and answer embeddings.

| Test name | Scenario | Expected |
|---|---|---|
| `ScoreAsync_IdenticalEmbeddings_ShouldReturnOne` | Question and answer vectors identical | ≈1.0f |
| `ScoreAsync_OrthogonalEmbeddings_ShouldReturnZero` | Orthogonal vectors (unrelated) | ≈0.0f |
| `ScoreAsync_AlwaysClampsBetweenZeroAndOne` | Any input | ∈[0, 1] |

---

### `ContextRecallScorerTests` (4 tests)

Tests `ContextRecallScorer.ScoreAsync()` — embedding similarity between expected answer and retrieved chunks.

| Test name | Scenario | Expected |
|---|---|---|
| `ScoreAsync_EmptySources_ShouldReturnZero` | No retrieved chunks | 0f (no embedding calls) |
| `ScoreAsync_PerfectMatch_ShouldReturnOne` | Expected answer vector matches chunk | ≈1.0f |
| `ScoreAsync_NoMatchingContext_ShouldReturnZero` | Vectors orthogonal | ≈0.0f |
| `ScoreAsync_AlwaysClampsBetweenZeroAndOne` | Any input | ∈[0, 1] |

---

### `EvaluationRunnerTests` (4 tests)

Tests `EvaluationRunner.RunAsync()` — end-to-end batch evaluation flow.

| Test name | Scenario | Expected |
|---|---|---|
| `RunAsync_EmptyDataset_ShouldReturnEmptyReport` | Empty Golden Dataset | Empty results, AvgOverall=0 |
| `RunAsync_SingleQuestion_ShouldProduceResult` | Single question full flow | Three-dimensional scores, Faithfulness≈0.8 |
| `RunAsync_FilterByQuestionIds_ShouldRunOnlySpecifiedQuestions` | Filter by QuestionIds | Only specified questions run |
| `RunAsync_QueryServiceThrows_ShouldIncludeEmptyResultWithoutCrashing` | RAG pipeline throws | Empty answer result, no crash |

---

## Running the Tests

```bash
# Run only Phase 6 evaluation tests
dotnet test tests/Veda.Evaluation.Tests/

# With coverage collection
dotnet test tests/Veda.Evaluation.Tests/ --collect:"XPlat Code Coverage"
```

---

## Storage Layer (Integration)

`EvalDatasetRepository` and `EvalReportRepository` use EF Core with the `Phase6_EvalDataset` migration (`20260322052023_Phase6_EvalDataset.cs`), which creates the `EvalQuestions` and `EvalRuns` tables. Integration tests for the storage layer can be added to a future `Veda.Storage.Tests` project.
