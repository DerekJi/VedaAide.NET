using Veda.Core.Options;
namespace Veda.Services;

/// <summary>
/// Plain-text chunker. Splits text using a sliding window according to the token budget in ChunkingOptions.
/// Phase 1 approximates token count by splitting on spaces/newlines (1 word ≈ 1.3 tokens).
/// </summary>
public sealed class TextDocumentProcessor : IDocumentProcessor
{
    /// <summary>
    /// Words per token estimate: 1 word ≈ 1.3 tokens (a conservative estimate to avoid exceeding the token budget).
    /// </summary>
    private const double WordsPerTokenEstimate = 1.3;

    public IReadOnlyList<DocumentChunk> Process(string content, string documentName, DocumentType documentType, string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var options = ChunkingOptions.ForDocumentType(documentType);
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var wordsPerChunk = (int)(options.TokenSize / WordsPerTokenEstimate);
        var overlapWords = (int)(options.OverlapTokens / WordsPerTokenEstimate);

        var chunks = new List<DocumentChunk>();
        var index = 0;
        var chunkIndex = 0;

        while (index < words.Length)
        {
            var end = Math.Min(index + wordsPerChunk, words.Length);
            var chunkText = string.Join(' ', words[index..end]);

            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = documentId,
                DocumentName = documentName,
                DocumentType = documentType,
                Content = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = new Dictionary<string, string>
                {
                    ["wordCount"] = (end - index).ToString(),
                    ["documentType"] = documentType.ToString()
                }
            });

            index += wordsPerChunk - overlapWords;
        }

        return chunks;
    }
}
