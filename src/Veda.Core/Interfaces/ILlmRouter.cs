namespace Veda.Core.Interfaces;

/// <summary>
/// LLM router: returns the matching IChatService implementation for a given QueryMode.
/// Simple → lightweight model (Ollama / GPT-4o-mini)
/// Advanced → heavyweight model (Ollama / DeepSeek)
/// When configuration is missing (e.g. the DeepSeek ApiKey is empty), Advanced automatically falls back to Simple.
/// </summary>
public interface ILlmRouter
{
    IChatService Resolve(QueryMode mode);
}
