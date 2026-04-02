namespace Veda.Core;

/// <summary>
/// 驱动动态分块策略的配置，由 DocumentType 决定。
/// DedupThreshold：摄取阶段语义去重的余弦相似度阈值，默认值 0.95；
/// Certificate 类型设为 0.70，避免内容相近的证书互相误杀。
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
        DocumentType.Certificate   => new(256,  32, DedupThreshold: 0.70f),
        _                          => new(512,  64)
    };
}
