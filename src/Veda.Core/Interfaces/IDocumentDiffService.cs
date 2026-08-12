namespace Veda.Core.Interfaces;

/// <summary>Document diff service contract for producing structured change summaries.</summary>
public interface IDocumentDiffService
{
    /// <summary>Compares the old and new versions of a document and produces a structured change summary.</summary>
    Task<DocumentChangeSummary> DiffAsync(
        string documentId,
        string oldContent,
        string newContent,
        CancellationToken ct = default);
}

/// <summary>Summarizes document changes.</summary>
public record DocumentChangeSummary(
    string DocumentId,
    int AddedChunks,
    int RemovedChunks,
    int ModifiedChunks,
    IReadOnlyList<string> ChangedTopics,
    DateTimeOffset ChangedAt);
