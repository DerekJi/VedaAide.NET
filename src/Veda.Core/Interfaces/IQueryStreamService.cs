namespace Veda.Core.Interfaces;

/// <summary>
/// 流式问答服务接口：先 yield sources，再逐 token yield LLM 输出，最后 yield done（含幻觉标志）。
/// </summary>
public interface IQueryStreamService
{
    /// <summary>流式查询：返回 SSE 格式的查询结果流。</summary>
    IAsyncEnumerable<RagStreamChunk> QueryStreamAsync(
        RagQueryRequest request,
        CancellationToken ct = default);
}
