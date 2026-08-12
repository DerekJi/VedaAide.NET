# VedaAide RAG Evaluation System Research Report

> **English Version** | [中文版](rag-evaluation-research.cn.html)

**Date:** 2026-08-12 | **Version:** v1.0

---

## Executive Summary

VedaAide has established a solid foundation for RAG evaluation with the `Veda.Evaluation` project and a three-dimensional scoring system. However, it lacks external dataset integration (HuggingFace), version management, and comprehensive evaluation metrics.

**Key Recommendations:**
1. **Code Structure:** No major refactoring needed. Add `IEvalDatasetProvider` interface to decouple data sources.
2. **Dataset Integration:** Support HuggingFace datasets (RAGAS, Natural Questions, MS MARCO) via Python preprocessing and dynamic loading.
3. **Evaluation System:** Expand from 3 dimensions to 9 dimensions by adding retrieval, generation, and efficiency metrics.
4. **Timeline:** 6 weeks to full deployment (Phase 1-5).

---

## Table of Contents

1. [Current Evaluation System Analysis](#current-state)
2. [Code Structure Recommendations](#architecture)
3. [HuggingFace Dataset Integration Plan](#dataset)
4. [Evaluation System Capabilities & Metrics](#evaluation-system)
5. [Implementation Roadmap](#implementation)
6. [Conclusions & Recommendations](#conclusion)

---

## 1. Current Evaluation System Analysis {#current-state}

### 1.1 Existing Evaluation Framework

VedaAide has built a relatively complete evaluation system foundation, including:

**✅ Implemented Core Components:**
- **Veda.Evaluation Project:** Independent evaluation module with `EvaluationRunner` and multiple Scorers
- **Three-Dimensional Scoring System:**
  - `Faithfulness` (0-1): Is the answer based only on retrieved context? (LLM judgment)
  - `AnswerRelevancy` (0-1): Is the answer on-topic? (Embedding similarity)
  - `ContextRecall` (0-1): Do retrieved results contain required information? (Embedding similarity vs. expected answer)
- **Golden Dataset Support:** `IEvalDatasetRepository` provides CRUD operations
- **Evaluation Report Generation:** `EvaluationReport` aggregates results and calculates overall score

### 1.2 Current Limitations

**⚠️ Gaps:**
- **Single Data Source:** Only manual REST API input; no auto-import mechanism
- **No External Dataset Support:** Cannot load from HuggingFace, Local, or other open sources
- **Incomplete Metrics:** Missing Precision@K, Recall@K, BLEU, ROUGE, F1-Score
- **No Version Management:** Cannot track and compare different model/prompt/strategy versions
- **No Automated Evaluation Pipeline:** Not integrated into CI/CD checkpoints

### 1.3 Architectural Strengths

**✨ Reusable Architecture:**
- **Strict DIP Compliance:** All interfaces in `Veda.Core`, implementations distributed
- **Clear Layering:** Core → Services → Storage → Entry Points
- **Excellent Test Coverage:** Already has unit tests with good Mock support
- **Flexible LLM Routing:** `LlmRouter` supports multi-model evaluation

---

## 2. Code Structure Recommendations {#architecture}

### 2.1 Current Architecture Status

The existing architecture already follows strict layering and separates RAG core algorithms from application layers:

```
Query Pipeline (Current):

API Layer (Veda.Api)
    ↓
QueryService (Veda.Services)
    ├→ SemanticEnhancer (query expansion)
    ├→ HybridRetriever (retrieval)
    ├→ ContextWindowBuilder (context construction)
    ├→ LlmRouter (LLM routing)
    └→ HallucinationGuard (hallucination detection)
    
Evaluation Layer (Veda.Evaluation)
    ├→ FaithfulnessScorer
    ├→ AnswerRelevancyScorer
    └→ ContextRecallScorer
```

### 2.2 Analysis: Do We Need Restructuring?

| Dimension | Current State | Recommendation |
|-----------|---------------|-----------------|
| Core Algorithm Isolation | ✅ Good. QueryService encapsulates logic | ⚡ Minor: Extract `RetrieverPipeline` interface |
| Application Layer Independence | ✅ Very Good. API calls via interfaces | ✅ No changes needed |
| Evaluation Framework Integration | ⚠️ Acceptable. Evaluation depends on Services | 🔨 Needed: `IEvalDatasetProvider` interface |
| Multi-Version Management | ❌ None. No version tracking | 🔨 Needed: `IEvalVersionStore` |
| Data Flow Independence | ⚠️ Moderate. Dataset coupled with Service | 🔨 Needed: `IEvalDatasetProvider` abstraction |

### 2.3 Recommended Minimal Change Strategy

**Core Idea:** Don't modify existing RAG implementation. Only add new abstractions in Evaluation layer to decouple data sources.

**Change Points:**

1. **Add interfaces in Veda.Core:**
```csharp
// Unified data source provider interface
public interface IEvalDatasetProvider
{
    Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,      // HuggingFace / Local / Database
        EvalDatasetConfig config,
        CancellationToken ct = default);
}

// Data source configuration
public record EvalDatasetConfig
{
    public string? RepoId { get; init; }           // huggingface repo
    public string? LocalPath { get; init; }        // local file path
    public string? Split { get; init; }            // "train" / "test" / "validation"
    public int? MaxRecords { get; init; }
}
```

2. **Implement multiple Providers in Veda.Services:**
   - `DatabaseEvalDatasetProvider`: Read from DB (existing logic)
   - `HuggingFaceEvalDatasetProvider`: Load and transform from HF
   - `LocalFileEvalDatasetProvider`: Read from local JSON/CSV

3. **Modify EvaluationRunner:** Accept `IEvalDatasetProvider` instead of hard-coding DB access

**Advantages:**
- ✅ Non-invasive: No changes to RAG query pipeline
- ✅ Highly extensible: Support new sources without modifying core code
- ✅ Easy to test: Mock `IEvalDatasetProvider`
- ✅ Backward compatible: Support both DB and external sources

### 2.4 Optional Advanced Change: Retrieval Pipeline Isolation

(Optional for long-term, but beneficial if needed for independent retrieval testing)

```csharp
public interface IRetrieverPipeline
{
    Task<IReadOnlyList<RankedChunk>> RetrieveAsync(
        string query,
        RagQueryRequest request,
        CancellationToken ct = default);
}
```

---

## 3. HuggingFace Dataset Integration {#dataset}

### 3.1 Recommended HuggingFace Datasets

| Dataset Name | Samples | Domain | Features | Recommendation |
|--------------|---------|--------|----------|-----------------|
| **RAGAS** (ragas-v1/code-generated) | 20K | General (code, docs) | Designed for RAG eval; Q-A-Context triples; high quality | ⭐⭐⭐⭐⭐ Priority 1 |
| **Natural Questions** | 323K | Open-domain QA | Real Google search logs; multi-hop reasoning | ⭐⭐⭐⭐ Priority 2 |
| **MS MARCO** | 1M | QA + Ranking | Large-scale Bing queries; retrieval-focused | ⭐⭐⭐⭐ Priority 3 |
| **SQuAD 2.0** | 150K | Reading Comprehension | Includes unanswerable questions | ⭐⭐⭐ Priority 4 |
| **CMMLU** (Chinese) | 14K | Multi-discipline Chinese | Chinese benchmark; convertible to QA | ⭐⭐⭐⭐ (Chinese) |
| **ZhQuAD** (Chinese) | 90K | Chinese Reading Comprehension | Chinese SQuAD-style; multi-domain | ⭐⭐⭐⭐ (Chinese) |

**Recommended Phased Approach:**
- **Phase 1 (Quick Validation):** RAGAS subset, ~500 samples
- **Phase 2 (Comprehensive Test):** RAGAS full + Natural Questions sample
- **Phase 3 (Long-term Tracking):** + Self-built Golden Dataset + periodic MS MARCO audit

### 3.2 Dataset Conversion & Preprocessing

**Location:** `scripts/eval-dataset-import.py`

**Core Functionality:**
```python
from datasets import load_dataset
import json

class DatasetImporter:
    def load_ragas(repo_id="ragas-v1/code-generated", split="test"):
        """Load RAGAS dataset"""
        dataset = load_dataset(repo_id, split=split)
        questions = []
        for item in dataset:
            questions.append({
                "id": f"ragas-{item['id']}",
                "question": item["question"],
                "expected_answer": item["answer"],
                "context": item["contexts"],
                "source": "ragas"
            })
        return questions

    def export_to_json(questions, output_file):
        """Export as JSON format"""
        with open(output_file, 'w') as f:
            json.dump(questions, f, ensure_ascii=False, indent=2)
```

**Usage Flow:**
1. Run: `python scripts/eval-dataset-import.py --dataset ragas --split test --output-format json`
2. Script downloads, transforms, validates
3. Output JSON or import via API to database

### 3.3 Standard Conversion Format

**Standard Format (EvalQuestion):**
```json
{
  "id": "ragas-001",
  "question": "What is machine learning?",
  "expected_answer": "Machine learning is ...",
  "context": ["Context chunk 1", "Context chunk 2"],
  "source_dataset": "ragas",
  "tags": ["beginner", "general"],
  "created_at": "2026-08-12T00:00:00Z"
}
```

### 3.4 Dynamic Loading Strategy

| Loading Method | Latency | Cost | Use Case |
|---|---|---|---|
| **Full Pre-download** | ~5-10 min (first time) | Disk ~500MB-2GB | CI/CD eval, offline |
| **Partial Cache** | ~1-2 min | Disk ~100-200MB | Dev/test iteration |
| **Streaming** | Variable | Network I/O | One-time report generation |

**Recommendation: Hybrid Approach**
- Default partial cache: 500 samples pre-downloaded to `data/eval-datasets/`
- Support `--refresh` flag for re-download
- Add to `.gitignore` (prevent repo bloat)

### 3.5 Multi-Scenario Data Coverage

| Scenario | Dataset | Data Source | Eval Focus |
|----------|---------|-------------|-----------|
| **Technical Docs** | RAGAS / StackOverflow QA | Github Docs, API Refs | Precision, context fidelity |
| **Open-Domain QA** | Natural Questions / MS MARCO | Real search logs | Retrieval recall, multi-hop |
| **Business Use Case** | Self-built Golden Dataset | Contracts, reports, meetings | Domain-specific accuracy |
| **Multi-Language** | CMMLU / ZhQuAD (Chinese) | Regional QA platforms | Cross-lingual accuracy |

---

## 4. Evaluation System Capabilities & Metrics {#evaluation-system}

### 4.1 Evaluation Dimension Expansion

**Current Dimensions (Keep):**

1. **Faithfulness (0-1):** Answer based only on context? (LLM judgment) | Weight: 30%
2. **Answer Relevancy (0-1):** Answer on-topic? (Embedding similarity) | Weight: 20%
3. **Context Recall (0-1):** Retrieved results contain required info? (Embedding similarity) | Weight: 20%

**Extended Dimensions (New):**

| Dimension | Metric Name | Definition | Calculation | Weight |
|-----------|-------------|-----------|-------------|--------|
| **Retrieval** | `Precision@K` | Relevant items in Top-K / K | # relevant / K | 10% |
| | `Recall@K` | Retrieved relevant / All relevant | # retrieved / # total | 10% |
| | `NDCG@K` | Ranking-aware recall (normalized) | DCG@K / IDCG@K | 5% |
| **Generation** | `ROUGE-L` | LCS ratio vs. expected answer | LCS / ref_len | 5% |
| | `Semantic F1` | Answer semantic precision (STS-based) | 2*P*R/(P+R) | 10% |
| | `Token Overlap` | Answer-expected token overlap | intersection / expected | 5% |
| **Efficiency** | `Latency (ms)` | End-to-end query time | milliseconds | Track |
| | `Token Cost` | LLM API cost | input_tokens + output_tokens | Track |

### 4.2 Composite Scoring

**Overall Score Formula:**
```
Overall Score = 
  0.30 × Faithfulness +
  0.20 × AnswerRelevancy +
  0.20 × ContextRecall +
  0.10 × Precision@5 +
  0.10 × Semantic F1 +
  0.05 × NDCG@10 +
  0.05 × ROUGE-L

Range: [0, 1]

Interpretation:
  > 0.8:    Excellent (production-ready)
  0.6-0.8:  Good (needs optimization)
  0.4-0.6:  Fair (has issues)
  < 0.4:    Poor (needs rebuild)
```

### 4.3 Evaluation System Core Modules

| Module | Features | Location |
|--------|----------|----------|
| **Dataset Management** | Import multi-source datasets (HF/Local/DB), versioning, stratified sampling | `IEvalDatasetProvider` (Core)<br/>`EvalDatasetService` (Services) |
| **Scorers** | Faithfulness, AnswerRelevancy, ContextRecall, Precision@K, NDCG, ROUGE (new) | `Veda.Evaluation/Scorers/` |
| **Evaluation Runner** | Batch execution, parallel/serial modes, error recovery | `EvaluationRunner` (Evaluation) |
| **Version Comparison** | Store metadata (model, prompt, timestamp), track changes, trend visualization | `IEvalVersionStore` (Core)<br/>`EvalVersionStore` (Storage) |
| **Report Generation** | Text summary, HTML dashboard, JSON export | `EvaluationReportGenerator` (Evaluation) |
| **CI/CD Integration** | Eval checkpoints (pre-merge), auto-regression detection | `Veda.Api` (eval endpoints) |

### 4.4 Visualization & Reporting

**Report Contents:**
- **Summary Cards:** Overall score + trend, dimension distribution, avg latency, total cost
- **Details Table:** Per-question scores, sorted by low-score-first
- **Comparison Analysis:** vs. baseline, vs. multiple models
- **Failure Case Analysis:** Root causes (retrieval fail / generation fail / hallucination)

### 4.5 Extensible Scorer Architecture

```csharp
// Veda.Core/Interfaces
public interface IEvalScorer
{
    string Name { get; }                    // "faithfulness", "precision@5"
    ScoreType Type { get; }                 // LLM / Embedding / Rule-based
    
    Task<float> ScoreAsync(
        EvalContext context,
        CancellationToken ct = default);
}

public record EvalContext
{
    public required string Question { get; init; }
    public required string ActualAnswer { get; init; }
    public required string ExpectedAnswer { get; init; }
    public required IReadOnlyList<string> RetrievedChunks { get; init; }
    public required IReadOnlyList<SourceReference> Sources { get; init; }
}
```

---

## 5. Implementation Roadmap {#implementation}

### 5.1 Phased Implementation Plan

| Phase | Timeline | Core Work | Deliverables |
|-------|----------|-----------|--------------|
| **Phase 1: Core Infrastructure** | 1-2 weeks | Add `IEvalDatasetProvider`, implement `HuggingFaceEvalDatasetProvider`, Python preprocessing script, unit tests | Load from HF, data format converter, complete tests |
| **Phase 2: Metrics Expansion** | 2-3 weeks | Implement Precision@K, Recall@K, NDCG, ROUGE-L, Semantic F1, version comparison | 6+ new Scorers, composite scoring, version comparison |
| **Phase 3: Visualization** | 2 weeks | HTML report generation, dashboard frontend, failure analysis UI | Beautiful HTML reports, interactive dashboard, CSV export |
| **Phase 4: CI/CD Integration** | 1-2 weeks | GitHub Actions workflow, eval checkpoints, regression detection | Auto PR evaluation, pass/fail criteria, report comments |
| **Phase 5: Optimization & Docs** | 1 week | Performance tuning, complete documentation, user guides | Full docs, example code, troubleshooting guide |

### 5.2 Phase 1 Details: Quick Start

**Goal:** 2 weeks to implement HF dataset import + basic evaluation

**Step 1. Add Interface (Veda.Core/Interfaces)**
```csharp
public interface IEvalDatasetProvider
{
    Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig config,
        CancellationToken ct = default);
}

public enum EvalDatasetSource { Database, HuggingFace, LocalFile }

public record EvalDatasetConfig
{
    public string? RepoId { get; init; }
    public string? LocalPath { get; init; }
    public string? Split { get; init; }
    public int? MaxRecords { get; init; }
    public bool PreferCache { get; init; } = true;
}
```

**Step 2. Implement Provider (Veda.Services)**
```csharp
public sealed class HuggingFaceEvalDatasetProvider : IEvalDatasetProvider
{
    public async Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetConfig config,
        CancellationToken ct)
    {
        var dataset = await HfHub.LoadDatasetAsync(config.RepoId, config.Split);
        var questions = new List<EvalQuestion>();
        
        int count = 0;
        foreach (var item in dataset)
        {
            if (config.MaxRecords.HasValue && count >= config.MaxRecords)
                break;
            
            questions.Add(ConvertToEvalQuestion(item));
            count++;
        }
        
        return questions;
    }
}
```

**Step 3. Python Preprocessing Script**
```bash
scripts/eval-dataset-import.py --dataset ragas --split test --output-format json
```

### 5.3 Tech Stack Selection

| Component | Recommended | Rationale |
|-----------|-------------|-----------|
| Dataset Loading | `huggingface-hub` (Python) or `Hugging.NET` (C#) | Official support; lightweight; cache-friendly |
| Text Similarity | `SentenceTransformers` (Python) via `EmbeddingService` | Reuse existing; efficient |
| ROUGE/BLEU | `rouge-score` (Python) or `ROUGE-dotnet` (C#) | Standard implementation; trustworthy |
| HTML Reports | `Scriban` (C# template) or `Razor` | ASP.NET ecosystem; dynamic generation |
| Visualization | `Chart.js` (frontend) generating HTML charts | Lightweight; no dependencies; beautiful |

---

## 6. Conclusions & Recommendations {#conclusion}

### 6.1 Core Conclusions

**✅ Good News: VedaAide Already Has Solid Foundation**
- Evaluation framework already exists
- Three-dimensional scoring ready
- Excellent layered architecture
- DIP strictly followed for easy extension

**🎯 Improvement Focus: Integration & Automation**
- ✅ No major refactoring needed
- ⚡ Lightweight changes: Add `IEvalDatasetProvider` only
- 🚀 Quick wins: Phase 1 (2 weeks) for HF + basic eval
- 📊 Gradual enhancement: Add metrics and visualization in phases

### 6.2 Immediate Action Items

**🚦 P0 Priority (Next Week)**
1. Design `IEvalDatasetProvider` interface
2. Implement `HuggingFaceEvalDatasetProvider`
3. Create Python preprocessing script

**📈 P1 Priority (Weeks 2-3)**
1. Implement 6+ new Scorers (Precision@K, NDCG, ROUGE, etc.)
2. Version comparison logic
3. Composite score calculation

**🎨 P2 Priority (Weeks 4-5)**
1. HTML report generation + visualization
2. CI/CD integration + PR evaluation
3. Complete documentation

### 6.3 Dataset Usage Recommendations

| Use Case | Dataset | Samples | Strategy |
|----------|---------|---------|----------|
| Quick Verification (Dev) | RAGAS subset | 100-200 | Local cache; fast iteration |
| Continuous Eval (Each Commit) | RAGAS full + Custom Golden | 500-1000 | CI/CD checkpoint; auto-run |
| Stress Test (Release) | Natural Questions / MS MARCO | 5000+ | Full evaluation; complete report |
| Chinese Scenarios | CMMLU / ZhQuAD / Custom | 500+ | Regular updates; domain-specific |

### 6.4 Recommended Metrics Hierarchy

**Phase 1 (Must Have):**
- Faithfulness
- Answer Relevancy  
- Context Recall

**Phase 2 (Important):**
- Precision@5 / Recall@5
- Semantic F1
- ROUGE-L

**Phase 3 (Nice to Have):**
- NDCG@10
- Cost/latency tracking
- Multi-hop reasoning detection

### 6.5 Success Criteria (6 Weeks Target)

✅ Support auto-import from 3+ HuggingFace datasets  
✅ Provide 9-dimension quantitative scoring  
✅ Generate beautiful HTML reports + version comparison  
✅ Integrate CI/CD (auto-eval every PR)  
✅ Complete documentation + runnable examples  
✅ Team can see quantified impact in 5-10 minutes per iteration

### 6.6 Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| HF data download slow | Partial cache + offline mode + mirror source config |
| High metric computation cost | Parallel evaluation + LLM call caching + subset validation |
| Long report generation | Async background tasks + incremental updates |
| Data quality issues | Validation pass + manual review support + version tracking |

### 6.7 Final Recommendation

**Code Structure:** No major changes needed. Only add `IEvalDatasetProvider` abstraction to maintain excellent layering.

**Dataset Strategy:** Phased approach from RAGAS 100 samples (2-3 days feedback) → 1000+ samples mixed datasets. Add ZhQuAD for Chinese.

**Evaluation System:** Keep existing 3 dimensions, add 6 more for retrieval/generation/efficiency layers = 9-dimension composite score.

**Implementation:** Phase 1 (2 weeks) HF import → Phase 2 (3 weeks) metrics expansion → Phase 3 (2 weeks) visualization & CI/CD.

**Expected Impact:** 5-10 minute feedback loop per change, enabling "test-driven RAG optimization" workflow.

---

## Appendix: Code Examples & References

### Sample: Modifying EvaluationRunner

```csharp
public sealed class EvaluationRunner
{
    private readonly IEvalDatasetProvider _datasetProvider;
    private readonly IQueryService _queryService;
    private readonly List<IEvalScorer> _scorers;

    public async Task<EvaluationReport> RunAsync(
        EvalRunOptions options,
        CancellationToken ct = default)
    {
        var questions = await _datasetProvider.LoadAsync(
            options.DatasetSource ?? EvalDatasetSource.Database,
            options.DatasetConfig,
            ct);

        var results = new List<EvalResult>();
        foreach (var question in questions)
        {
            var response = await _queryService.QueryAsync(
                new RagQueryRequest { Question = question.Question },
                ct);

            var scores = new Dictionary<string, float>();
            foreach (var scorer in _scorers)
            {
                scores[scorer.Name] = await scorer.ScoreAsync(
                    new EvalContext
                    {
                        Question = question.Question,
                        ActualAnswer = response.Answer,
                        ExpectedAnswer = question.ExpectedAnswer,
                        RetrievedChunks = response.Sources.Select(s => s.ChunkContent).ToList(),
                        Sources = response.Sources
                    },
                    ct);
            }

            results.Add(new EvalResult
            {
                QuestionId = question.Id,
                Question = question.Question,
                ExpectedAnswer = question.ExpectedAnswer,
                ActualAnswer = response.Answer,
                Scores = scores
            });
        }

        return new EvaluationReport { Results = results };
    }
}
```

---

**Report Generated:** 2026-08-12  
**Format:** Markdown (HTML version also available)  
**Status:** Ready for team review and implementation planning
