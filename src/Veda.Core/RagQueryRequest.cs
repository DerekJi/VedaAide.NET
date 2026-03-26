namespace Veda.Core;

public record RagQueryRequest
{
    public string Question              { get; init; } = string.Empty;
    public DocumentType? FilterDocumentType { get; init; }
    public int TopK                     { get; init; } = 5;
    public float MinSimilarity          { get; init; } = 0.3f;
    /// <summary>仅返回在此时间之后摄取的文档块（含边界），null 表示不限制。</summary>
    public DateTimeOffset? DateFrom     { get; init; }
    /// <summary>仅返回在此时间之前摄取的文档块（含边界），null 表示不限制。</summary>
    public DateTimeOffset? DateTo       { get; init; }
    /// <summary>LLM 复杂度模式：Simple（默认）或 Advanced（深度分析）。</summary>
    public QueryMode Mode               { get; init; } = QueryMode.Simple;
}
