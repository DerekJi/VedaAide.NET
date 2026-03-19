namespace Veda.Core;

public class RagQueryRequest
{
    public string Question     { get; set; } = string.Empty;
    public DocumentType? FilterDocumentType { get; set; }
    public int TopK            { get; set; } = 5;
    public float MinSimilarity { get; set; } = 0.6f;
}
