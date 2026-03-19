namespace Veda.Core;

/// <summary>
/// 驱动动态分块策略的配置，由 DocumentType 决定。
/// </summary>
public record ChunkingOptions(int TokenSize, int OverlapTokens)
{
    public static ChunkingOptions ForDocumentType(DocumentType type) => type switch
    {
        DocumentType.BillInvoice  => new(256,  32),
        DocumentType.Specification => new(1024, 128),
        DocumentType.Report       => new(512,  64),
        _                         => new(512,  64)
    };
}
