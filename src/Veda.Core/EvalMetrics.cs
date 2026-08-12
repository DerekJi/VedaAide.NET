namespace Veda.Core;

/// <summary>Three-dimensional metric scores for a single question evaluation, each in the range [0, 1].</summary>
public record EvalMetrics
{
    /// <summary>Faithfulness: whether the answer relies only on the retrieved context (LLM judgment).</summary>
    public float Faithfulness { get; init; }

    /// <summary>Answer relevancy: whether the answer is on-topic (cosine similarity between question and answer embeddings).</summary>
    public float AnswerRelevancy { get; init; }

    /// <summary>Context recall: whether the expected answer can be derived from the retrieved context (embedding similarity).</summary>
    public float ContextRecall { get; init; }

    /// <summary>Mean of the three dimensions: overall score.</summary>
    public float Overall => (Faithfulness + AnswerRelevancy + ContextRecall) / 3f;
}
