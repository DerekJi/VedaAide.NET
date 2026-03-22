namespace Veda.Prompts;

/// <summary>
/// 基础 CoT 实现：在用户消息中注入"列出推理步骤，再给出结论"的引导。
/// 适用于包含数值计算、时间推断、多步逻辑的复杂问题场景。
/// </summary>
public sealed class ChainOfThoughtStrategy : IChainOfThoughtStrategy
{
    private const string CoTInstruction = """
        请按以下步骤作答：
        1. 从 Context 中找出与问题直接相关的信息片段。
        2. 分析这些信息，逐步推导出答案。
        3. 给出最终结论。

        """;

    public string Enhance(string question, string context)
    {
        return $"Context:\n{context}\n\n{CoTInstruction}Question: {question}";
    }
}
