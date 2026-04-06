# Stage 3 — Development Plan (English)

## 1. Overview

This document describes the architecture and implementation plan for Stage 3 of VedaAide.NET, focusing on the semantic enhancement layer, ingestion/retrieval alignment, and extensibility.

---

## 2. Semantic Enhancement Layer

### 2.1 Interface Design

```csharp
// New in Veda.Core.SemanticEnhancementResult
public sealed record SemanticEnhancementResult
{
    /// <summary>Alias tags matched by Tags rules</summary>
    public required IReadOnlyList<string> AliasTags { get; init; }

    /// <summary>All detected terms and their synonyms from Vocabulary</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DetectedTermsWithSynonyms { get; init; }

    /// <summary>Enriched content: original + all detected terms and synonyms</summary>
    public required string EnrichedContent { get; init; }
}

// New in Veda.Core.Interfaces
public interface ISemanticEnhancer
{
    // Used during ingestion: applies both Vocabulary (terms+synonyms) and Tags (pattern matching)
    // Returns unified semantic enhancement metadata, ensuring ingestion/retrieval alignment
    Task<SemanticEnhancementResult> GetEnhancedMetadataAsync(
        string content, CancellationToken ct = default);

    // Query expansion: maps abbreviations/custom terms to canonical synonyms
    Task<string> ExpandQueryAsync(string query, CancellationToken ct = default);

    // Backward compatibility: extracts alias tags from GetEnhancedMetadataAsync
    Task<IReadOnlyList<string>> GetAliasTagsAsync(
        string content, CancellationToken ct = default);
}

// Config-driven personal vocabulary implementation
public class PersonalVocabularyEnhancer : ISemanticEnhancer
{
    // Ingestion: GetEnhancedMetadataAsync applies both Vocabulary and Tags
    // Retrieval: ExpandQueryAsync applies the same Vocabulary expansion logic
    // Vocabulary source: JSON config file or user API upload, decoupled from core code
}
```

### 2.2 Integration Points

- `QueryService.QueryAsync` → Calls `ISemanticEnhancer.ExpandQueryAsync` before generating embeddings
- `DocumentIngestService.IngestAsync` → Calls `ISemanticEnhancer.GetEnhancedMetadataAsync` during chunk ingestion, writes `aliasTags` and `detectedTerms` to metadata
- `ISemanticEnhancer` default implementation is `NoOpSemanticEnhancer` (pass-through); vocabulary feature is enabled via config
- Vocabulary file path is configured via `Veda:Semantics:VocabularyFilePath`, provided by the user
- **Symmetry Principle**: GetEnhancedMetadataAsync and ExpandQueryAsync use the same Vocabulary, ensuring terms detected at ingestion are also found at retrieval

---

## 3. Personal Vocabulary File Format (JSON)

```json
{
  "vocabulary": [
    { "term": "bg", "synonyms": ["background info", "context"] },
    { "term": "Q4", "synonyms": ["fourth quarter"] }
  ],
  "tags": [
    { "pattern": "invoice|payment", "labels": ["finance", "billing"] },
    { "pattern": "health|checkup", "labels": ["health", "medical record"] }
  ]
}
```

---

## 4. Status Checklist

- [x] `ISemanticEnhancer` interface + `PersonalVocabularyEnhancer` + `NoOpSemanticEnhancer` implemented
- [x] `QueryService` integrated with `ISemanticEnhancer.ExpandQueryAsync`
- [x] `DocumentIngestService` integrated with `ISemanticEnhancer.GetEnhancedMetadataAsync`

---

## 5. Design Principles

- **SRP (Single Responsibility Principle)**: Each class has a single concern
- **Symmetry**: Ingestion and retrieval use the same semantic rules
- **Backward Compatibility**: `GetAliasTagsAsync()` remains for legacy code
- **DRY**: Unified SemanticEnhancementResult avoids data structure duplication

---

## 6. References

- [02-ingest-flow.en.md](../rag-internals/02-ingest-flow.en.md)
- [configuration.en.md](../configuration/configuration.en.md)
