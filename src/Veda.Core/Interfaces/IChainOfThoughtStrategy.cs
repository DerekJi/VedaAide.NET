namespace Veda.Core.Interfaces;

/// <summary>
/// Chain-of-Thought prompting strategy: injects reasoning-step guidance into the user message to improve inference quality on complex questions.
/// </summary>
public interface IChainOfThoughtStrategy
{
    /// <summary>
    /// Combines the question and context into a full user message with reasoning guidance.
    /// </summary>
    string Enhance(string question, string context);
}
