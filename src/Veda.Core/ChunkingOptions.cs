namespace Veda.Core;

/// <summary>
/// 驱动动态分块策略的配置，由 DocumentType 决定。
/// DedupThreshold：摄取阶段语义去重的余弦相似度阈值，默认值 0.95；
/// 值越高表示去重越宽松（只有相似度极高时才判定为近重复）。
/// Certificate 类型设为 1.0（实际上禁用语义去重），原因：同类证书（英语/数学/科学）
/// 的嵌入向量余弦相似度极高（> 0.97），设为 0.70 时不同科目的证书会互相误杀。
/// 去重仍通过 ContentHash（SHA-256）保证完全相同内容不重复存储。
/// </summary>
public record ChunkingOptions(int TokenSize, int OverlapTokens, float DedupThreshold = RagDefaults.SimilarityDedupThreshold)
{
    public static ChunkingOptions ForDocumentType(DocumentType type) => type switch
    {
        DocumentType.BillInvoice   => new(256,  32),
        DocumentType.Specification => new(1024, 128),
        DocumentType.Report        => new(512,  64),
        DocumentType.PersonalNote  => new(256,  32),
        DocumentType.RichMedia     => new(512,  64),
        DocumentType.Certificate   => new(256,  32, DedupThreshold: 1.0f),
        _                          => new(512,  64)
    };
}
