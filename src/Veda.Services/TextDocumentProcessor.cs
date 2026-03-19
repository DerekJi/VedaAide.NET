namespace Veda.Services;

/// <summary>
/// 纯文本分块器。按 ChunkingOptions 指定的 token 预算进行滑动窗口分割。
/// Phase 1 以空格/换行为分词边界近似 token 数量（1 word ≈ 1.3 tokens）。
/// </summary>
public sealed class TextDocumentProcessor : IDocumentProcessor
{
    /// <summary>
    /// 每个 token 约对应的单词数倒数：1 word ≈ 1.3 tokens（保守估算，避免超出 token 预算）。
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
