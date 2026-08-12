namespace Veda.Core;

/// <summary>
/// Thrown when an external quota (e.g. the Azure AI Document Intelligence free tier) is exhausted.
/// Callers can catch this exception and fall back to an alternative implementation (e.g. a Vision model).
/// </summary>
public sealed class QuotaExceededException : Exception
{
    public QuotaExceededException(string message) : base(message) { }
    public QuotaExceededException(string message, Exception inner) : base(message, inner) { }
}
