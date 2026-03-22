namespace Veda.Core.Interfaces;

/// <summary>
/// Chain-of-Thought 提示策略：在用户消息中注入推理步骤引导，提升复杂问题的推断质量。
/// </summary>
public interface IChainOfThoughtStrategy
{
    /// <summary>
    /// 将问题和上下文组合为带推理引导的完整用户消息。
    /// </summary>
    string Enhance(string question, string context);
}
