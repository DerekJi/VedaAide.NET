namespace Veda.Core.Interfaces;

/// <summary>
/// 问答查询服务契约（同步查询，返回完整答案）。
/// ISP：与摄取操作分离、与流式查询分离。
/// </summary>
public interface IQueryService
{
    Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default);
}
