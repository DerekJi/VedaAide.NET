namespace Veda.Core;

/// <summary>
/// 当外部配额（如 Azure AI Document Intelligence 免费层）耗尽时抛出。
/// 调用方可捕获此异常并降级到备用实现（如 Vision 模型）。
/// </summary>
public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message) { }
    public QuotaExceededException(string message, Exception inner) : base(message, inner) { }
}
