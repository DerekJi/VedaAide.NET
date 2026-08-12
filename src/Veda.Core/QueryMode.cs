namespace Veda.Core;

/// <summary>
/// LLM query complexity mode.
/// Simple: uses a lightweight/fast model (e.g. GPT-4o-mini) for everyday Q&A.
/// Advanced: uses a heavyweight model (e.g. DeepSeek) for complex analysis and multi-step reasoning tasks.
/// Explicitly specified by the caller based on business semantics; defaults to Simple.
/// </summary>
public enum QueryMode
{
    Simple,
    Advanced
}
