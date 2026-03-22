namespace Veda.Core;

/// <summary>单次问题评估的三维指标分数，值域均为 [0, 1]。</summary>
public record EvalMetrics
{
    /// <summary>忠实度：回答是否仅依赖检索到的上下文（LLM 判断）。</summary>
    public float Faithfulness { get; init; }

    /// <summary>答案相关性：回答是否切题（问题与答案 Embedding 余弦相似度）。</summary>
    public float AnswerRelevancy { get; init; }

    /// <summary>上下文召回率：期望答案是否能从检索到的上下文中推导出来（Embedding 相似度）。</summary>
    public float ContextRecall { get; init; }

    /// <summary>三维均值：综合评分。</summary>
    public float Overall => (Faithfulness + AnswerRelevancy + ContextRecall) / 3f;
}
