using Veda.Api.Models;

namespace Veda.Api.GraphQL;

/// <summary>
/// HotChocolate GraphQL Mutation type.
/// </summary>
public sealed class Mutation
{
    /// <summary>
    /// Ingests a document: chunking → Embedding → similarity dedup → storage.
    /// </summary>
    public async Task<IngestResult> IngestDocumentAsync(
        string content,
        string documentName,
        [Service] IDocumentIngestor ingestor,
        string? documentType = null,
        CancellationToken ct = default)
    {
        var docType = DocumentTypeParser.ParseOrDefault(documentType);
        return await ingestor.IngestAsync(content, documentName, docType, ct: ct);
    }
}
