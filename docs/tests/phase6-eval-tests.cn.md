# 阶段六：AI 评估体系测试方案

> 中文版 · English version: [phase6-eval-tests.en.md](phase6-eval-tests.en.md)

## 概览

本文档描述阶段六（AI 评估体系）的测试策略、覆盖范围及各测试类说明。

**测试项目**：`tests/Veda.Evaluation.Tests/`
**被测模块**：`src/Veda.Evaluation/`（Scorers + EvaluationRunner）

---

## 测试策略

- **全部使用 Mock**：`IChatService`（LLM）和 `IEmbeddingService` 均通过 Moq 替换，零外部依赖，运行速度极快。
- **覆盖路径**：正常路径 + 边界值（空列表、满分/零分）+ 异常/容错路径。
- **覆盖率要求**：`Veda.Evaluation` ≥ 90%（当前实测：94.5% 行覆盖 / 100% 分支覆盖）。

---

## 测试类说明

### `FaithfulnessScorerTests`（6 个测试）

测试 `FaithfulnessScorer.ScoreAsync()` — LLM 忠实度评分。

| 测试名称 | 场景 | 期望结果 |
|---|---|---|
| `ScoreAsync_LlmReturnsOne_ShouldReturnOnePointZero` | LLM 返回 "1.0" | 1.0f |
| `ScoreAsync_LlmReturnsZero_ShouldReturnZero` | LLM 返回 "0.0" | 0.0f |
| `ScoreAsync_LlmReturnsPartialScore_ShouldClampToRange` | LLM 返回 "0.65" | ≈0.65f |
| `ScoreAsync_LlmReturnsOutOfRangeHigh_ShouldClampToOne` | LLM 返回 "1.5"（越界） | 1.0f（Clamp） |
| `ScoreAsync_LlmReturnsGarbage_ShouldReturnZero` | LLM 返回非数字字符串 | 0.0f（兜底） |
| `ScoreAsync_LlmThrows_ShouldReturnZero` | LLM 抛出异常 | 0.0f（容错） |

---

### `AnswerRelevancyScorerTests`（3 个测试）

测试 `AnswerRelevancyScorer.ScoreAsync()` — 问答 Embedding 余弦相似度。

| 测试名称 | 场景 | 期望结果 |
|---|---|---|
| `ScoreAsync_IdenticalEmbeddings_ShouldReturnOne` | 问题与答案向量相同 | ≈1.0f |
| `ScoreAsync_OrthogonalEmbeddings_ShouldReturnZero` | 向量正交（完全不相关） | ≈0.0f |
| `ScoreAsync_AlwaysClampsBetweenZeroAndOne` | 任意输入 | ∈[0, 1] |

---

### `ContextRecallScorerTests`（4 个测试）

测试 `ContextRecallScorer.ScoreAsync()` — 期望答案与检索块的 Embedding 相似度。

| 测试名称 | 场景 | 期望结果 |
|---|---|---|
| `ScoreAsync_EmptySources_ShouldReturnZero` | 检索块列表为空 | 0f（不调用 Embedding） |
| `ScoreAsync_PerfectMatch_ShouldReturnOne` | 期望答案与检索块向量相同 | ≈1.0f |
| `ScoreAsync_NoMatchingContext_ShouldReturnZero` | 向量完全正交 | ≈0.0f |
| `ScoreAsync_AlwaysClampsBetweenZeroAndOne` | 任意输入 | ∈[0, 1] |

---

### `EvaluationRunnerTests`（4 个测试）

测试 `EvaluationRunner.RunAsync()` — 端到端批量评估流程。

| 测试名称 | 场景 | 期望结果 |
|---|---|---|
| `RunAsync_EmptyDataset_ShouldReturnEmptyReport` | Golden Dataset 为空 | 报告结果为空，AvgOverall=0 |
| `RunAsync_SingleQuestion_ShouldProduceResult` | 单个问题完整流程 | 结果包含三维评分，Faithfulness≈0.8 |
| `RunAsync_FilterByQuestionIds_ShouldRunOnlySpecifiedQuestions` | QuestionIds 过滤 | 仅运行指定 ID 的问题 |
| `RunAsync_QueryServiceThrows_ShouldIncludeEmptyResultWithoutCrashing` | RAG 管道抛出异常 | 返回空答案结果，不崩溃 |

---

## 运行方式

```bash
# 只运行 Phase 6 评估测试
dotnet test tests/Veda.Evaluation.Tests/

# 带覆盖率收集
dotnet test tests/Veda.Evaluation.Tests/ --collect:"XPlat Code Coverage"
```

---

## 数据库相关（集成层）

Phase 6 的 `EvalDatasetRepository` 和 `EvalReportRepository` 通过 EF Core 集成测试覆盖（位于 `Veda.Storage.Tests`，未来扩展）。迁移文件为 `20260322052023_Phase6_EvalDataset.cs`，包含 `EvalQuestions` 和 `EvalRuns` 两张表。
