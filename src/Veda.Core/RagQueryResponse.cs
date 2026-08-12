namespace Veda.Core;

public record RagQueryResponse
{
    public string Answer              { get; init; } = string.Empty;
    public List<SourceReference> Sources { get; init; } = [];
    /// <summary>true indicates a potential hallucination was detected; set by the anti-hallucination layer, the frontend can use it to decide whether to show a warning.</summary>
    public bool IsHallucination       { get; init; }
    public float AnswerConfidence     { get; init; }
    /// <summary>Structured reasoning output (populated only when StructuredOutput=true in the request).</summary>
    public StructuredFinding? StructuredOutput { get; init; }
}

public record SourceReference
{
    public string DocumentName { get; init; } = string.Empty;
    public string ChunkContent { get; init; } = string.Empty;
    public float  Similarity   { get; init; }
    public string? ChunkId     { get; init; }
    public string? DocumentId  { get; init; }
}
