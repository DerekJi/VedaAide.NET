namespace Veda.Core.Interfaces;

/// <summary>
/// 问答查询服务契约（读操作）。
/// ISP：与摄取操作分离，QueryController 只依赖此接口。
/// </summary>
public interface IQueryService
{
    Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// 流式问答：先返回检索到的来源，再逐 token 流式输出 LLM 回答。
    /// </summary>
    IAsyncEnumerable<RagStreamChunk> QueryStreamAsync(RagQueryRequest request, CancellationToken ct = default);
}
