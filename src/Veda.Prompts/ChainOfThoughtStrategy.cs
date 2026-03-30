namespace Veda.Prompts;

/// <summary>
/// 基础 CoT 实现：在用户消息中注入"列出推理步骤，再给出结论"的引导。
/// 自动检测问题语言，选用中文或英文引导语，避免引导语语言影响 LLM 回答语言。
/// </summary>
public sealed class ChainOfThoughtStrategy : IChainOfThoughtStrategy
{
    private const string CoTInstructionZh = """
        请按以下步骤作答：
        1. 从 Context 中找出与问题直接相关的信息片段。
        2. 分析这些信息，逐步推导出答案。
        3. 给出最终结论。

        """;

    private const string CoTInstructionEn = """
        Please follow these steps to answer:
        1. Identify information in the Context that is directly relevant to the question.
        2. Analyze the information and reason step by step.
        3. Provide a final conclusion.

        """;

    public string Enhance(string question, string context)
    {
        var instruction = IsChinese(question) ? CoTInstructionZh : CoTInstructionEn;
        return $"Context:\n{context}\n\n{instruction}Question: {question}";
    }

    /// <summary>
    /// 简单启发式语言检测：CJK 字符占比超过 30% 则视为中文。
    /// </summary>
    private static bool IsChinese(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var cjkCount = text.Count(c => c >= '\u4E00' && c <= '\u9FFF');
        return cjkCount * 3 > text.Length; // CJK 占比 > 33%
    }
}
