namespace Veda.Core;

public record RagQueryRequest
{
    public string Question              { get; init; } = string.Empty;
    public DocumentType? FilterDocumentType { get; init; }
    public int TopK                     { get; init; } = 5;
    public float MinSimilarity          { get; init; } = RagDefaults.DefaultMinSimilarity;
    /// <summary>Only returns document chunks ingested after this time (inclusive); null means no limit.</summary>
    public DateTimeOffset? DateFrom     { get; init; }
    /// <summary>Only returns document chunks ingested before this time (inclusive); null means no limit.</summary>
    public DateTimeOffset? DateTo       { get; init; }
    /// <summary>LLM complexity mode: Simple (default) or Advanced (deep analysis).</summary>
    public QueryMode Mode               { get; init; } = QueryMode.Simple;
    /// <summary>Knowledge scope filter; null means no filtering — retrieves all visible documents.</summary>
    public KnowledgeScope? Scope        { get; init; }
    /// <summary>Whether to enable structured reasoning output (including Evidence[] and Confidence).</summary>
    public bool StructuredOutput        { get; init; } = false;
    /// <summary>Current user ID (optional); enables personalized feedback boost when provided.</summary>
    public string? UserId               { get; init; }
    /// <summary>
    /// Temporary context (Ephemeral RAG): text extracted from a file uploaded by the frontend, injected directly into the prompt without writing to the database.
    /// null means no temporary attachment.
    /// </summary>
    public string? EphemeralContext     { get; init; }
}
