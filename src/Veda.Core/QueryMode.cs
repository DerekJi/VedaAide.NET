namespace Veda.Core;

/// <summary>
/// LLM 查询复杂度模式。
/// Simple：使用轻量/快速模型（如 GPT-4o-mini）处理日常问答。
/// Advanced：使用重量级模型（如 DeepSeek）处理复杂分析、多步推理任务。
/// 由调用方根据业务语义显式指定，默认 Simple。
/// </summary>
public enum QueryMode
{
    Simple,
    Advanced
}
