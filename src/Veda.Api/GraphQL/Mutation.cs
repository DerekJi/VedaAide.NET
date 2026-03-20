using Veda.Api.Models;

namespace Veda.Api.GraphQL;

/// <summary>
/// HotChocolate GraphQL Mutation 类型。
/// </summary>
public sealed class Mutation
{
    /// <summary>
    /// 摄取文档：分块 → Embedding → 相似度去重 → 存储。
    /// </summary>
    public async Task<IngestResult> IngestDocumentAsync(
        string content,
        string documentName,
        [Service] IDocumentIngestor ingestor,
        string? documentType = null,
        CancellationToken ct = default)
    {
        var docType = DocumentTypeParser.ParseOrDefault(documentType);
        return await ingestor.IngestAsync(content, documentName, docType, ct);
    }
}
