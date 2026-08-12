
using Veda.Core.Extensions;
namespace Veda.Prompts;

/// <summary>
/// Basic CoT implementation: injects a "list reasoning steps, then give a conclusion" instruction into the user message.
/// Automatically detects the question language and picks a Chinese or English instruction,
/// so the instruction language does not affect the LLM's answer language.
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
        var instruction = question.IsChinese() ? CoTInstructionZh : CoTInstructionEn;
        return $"Context:\n{context}\n\n{instruction}Question: {question}";
    }
}
