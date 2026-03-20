namespace Veda.Core;

/// <summary>
/// 流式查询的单个输出片段。
/// type = "sources" 时携带来源列表（在 LLM 开始回答前发送）；
/// type = "token"   时携带 LLM 生成的单个/多个 token 文本；
/// type = "done"    时表示流结束，携带 isHallucination 结果。
/// </summary>
public record RagStreamChunk
{
    /// <summary>片段类型：sources | token | done</summary>
    public required string Type { get; init; }

    /// <summary>LLM 输出的文本片段（Type = "token" 时有值）。</summary>
    public string? Token { get; init; }

    /// <summary>检索到的来源列表（Type = "sources" 时有值）。</summary>
    public IReadOnlyList<SourceReference>? Sources { get; init; }

    /// <summary>答案置信度（Type = "done" 时有值）。</summary>
    public float? AnswerConfidence { get; init; }

    /// <summary>是否疑似幻觉（Type = "done" 时有值）。</summary>
    public bool? IsHallucination { get; init; }
}
