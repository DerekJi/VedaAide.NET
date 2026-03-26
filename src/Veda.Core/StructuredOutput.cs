namespace Veda.Core;

/// <summary>结构化推理输出，包含结论类型、证据链和置信度。</summary>
public record StructuredFinding(
    FindingType Type,
    string Summary,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<EvidenceItem>? CounterEvidence,
    double Confidence,
    string? UncertaintyNote);

/// <summary>推理结论类型。</summary>
public enum FindingType
{
    Information,  // 一般信息
    Warning,      // 需要关注的警示
    Conflict,     // 知识库中存在相互矛盾的信息
    HighRisk      // 高风险决策相关
}

/// <summary>支持结论的单条证据，携带原文片段和相关度。</summary>
public record EvidenceItem(
    string DocumentId,
    string DocumentName,
    string Snippet,
    double RelevanceScore);
