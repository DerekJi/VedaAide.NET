namespace Veda.Core;

public record RagQueryResponse
{
    public string Answer              { get; init; } = string.Empty;
    public List<SourceReference> Sources { get; init; } = [];
    /// <summary>true 表示检测到潜在幻觉；由防幻觉层设置，前端可据此决策是否显示警告。</summary>
    public bool IsHallucination       { get; init; }
    public float AnswerConfidence     { get; init; }
    /// <summary>结构化推理输出（仅当请求中 StructuredOutput=true 时填充）。</summary>
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
