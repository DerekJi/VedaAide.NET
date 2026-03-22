# Dynamic Chunking Strategy: Why Not Fixed-Size Chunks

**Project**: VedaAide.NET  
**Files**: `src/Veda.Core/ChunkingOptions.cs`, `src/Veda.Core/DocumentType.cs`  
**Phase**: Phase 1

> 中文版见 [chunking-strategy.cn.md](chunking-strategy.cn.md)

---

## Background

The introductory RAG implementation typically splits all documents by a fixed token count (e.g., 512). This is simple, but the results vary drastically across different content types.

---

## Core Insight: Document Type Determines Optimal Granularity

Different documents have different **semantic density** and **retrieval goals**:

| Document Type | Content Characteristics | Retrieval Goal | Optimal Granularity |
|---|---|---|---|
| Bill/Invoice | Each line contains an independent amount, date, or item | Precisely extract one field | Small (256 tokens) |
| Specification/PDS | Clauses are highly interdependent — splitting breaks context | Understand the complete clause semantics | Large (1024 tokens) |
| Report/Personal Note | Paragraphs are relatively independent, moderately connected | Topic-based retrieval | Medium (512 tokens) |

If a bill is split at 512 tokens, a single chunk may mix data from multiple invoices — asking "what's my March water bill?" is likely to retrieve the wrong numbers. If a specification document is split at 256 tokens, clauses are truncated and the LLM receives incomplete semantics, degrading answer quality.

---

## Implementation

Managed centrally through the `DocumentType` enum + factory method in `ChunkingOptions`:

```csharp
// ChunkingOptions.cs
public static ChunkingOptions ForDocumentType(DocumentType type) => type switch
{
    DocumentType.BillInvoice   => new(256,  32),   // small chunks, little overlap
    DocumentType.Specification => new(1024, 128),  // large chunks, more overlap
    DocumentType.Report        => new(512,  64),
    DocumentType.PersonalNote  => new(256,  32),   // same as bills — short and precise
    _                          => new(512,  64)
};
```

`TokenSize` is the primary chunk body size; `OverlapTokens` is the sliding window overlap between adjacent chunks — overlap prevents semantic boundaries from falling exactly on a cutoff point, avoiding "a sentence cut in half" where neither half can independently express a complete meaning.

---

## Overlap Trade-offs

Larger overlap → higher retrieval hit rate, but:
- Storage grows (duplicate tokens stored twice)
- Deduplication logic needs a higher similarity threshold to recognize near-duplicate chunks

The current choice of `overlap = chunkSize / 8` (about 12.5%) is at the lower bound of industry experience values, appropriate for local deployments where storage is not tight.

---

## DocumentType: Automatic Detection vs. User-Specified

Two modes are currently supported:
1. **User explicitly specifies** at ingestion time (via the `documentType` field in the API)
2. **Not specified → defaults to `Other`** (512-token medium granularity)

Phase 4 can add auto-classification: infer `DocumentType` from filename/content features (keywords, structure) without requiring user input.

---

## Interview Talking Points

- Fixed vs. semantic vs. document-type-based dynamic chunking — trade-offs between the three strategies
- The role of overlap: why RAG chunking is not simply "cut by length"
- How chunking strategy affects embedding quality: too short → semantically incomplete vectors; too long → vectors are diluted, retrieval precision drops
- Why `DocumentType` as a domain concept lives in `Veda.Core`, not `Veda.Api`: domain knowledge should not be coupled to the API layer
