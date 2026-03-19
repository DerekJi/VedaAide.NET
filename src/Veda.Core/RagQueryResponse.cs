namespace Veda.Core;

public class RagQueryResponse
{
    public string Answer        { get; set; } = string.Empty;
    public List<SourceReference> Sources { get; set; } = new();
    public bool IsHallucination { get; set; }
    public float AnswerConfidence { get; set; }
}

public class SourceReference
{
    public string DocumentName { get; set; } = string.Empty;
    public string ChunkContent { get; set; } = string.Empty;
    public float Similarity    { get; set; }
}
