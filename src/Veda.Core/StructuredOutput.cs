namespace Veda.Core;

/// <summary>Structured reasoning output, containing the conclusion type, chain of evidence, and confidence.</summary>
public record StructuredFinding(
    FindingType Type,
    string Summary,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<EvidenceItem>? CounterEvidence,
    double Confidence,
    string? UncertaintyNote);

/// <summary>Reasoning conclusion type.</summary>
public enum FindingType
{
    Information,  // general information
    Warning,      // a warning that requires attention
    Conflict,     // conflicting information exists in the knowledge base
    HighRisk      // related to high-risk decisions
}

/// <summary>A single piece of evidence supporting the conclusion, carrying the original snippet and relevance.</summary>
public record EvidenceItem(
    string DocumentId,
    string DocumentName,
    string Snippet,
    double RelevanceScore);
