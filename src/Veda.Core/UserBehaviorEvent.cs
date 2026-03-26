namespace Veda.Core;

/// <summary>用户行为事件，记录用户与 RAG 系统的交互动作。</summary>
public record UserBehaviorEvent(
    string UserId,
    string SessionId,
    BehaviorType Type,
    string? RelatedDocumentId,
    string? RelatedChunkId,
    string? Query,
    DateTimeOffset OccurredAt,
    IDictionary<string, object>? Payload = null)
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
}

/// <summary>用户行为类型。</summary>
public enum BehaviorType
{
    ResultAccepted,   // 用户采纳了推荐结果
    ResultRejected,   // 用户标记结果无关
    AnswerEdited,     // 用户修改了 AI 输出
    SourceClicked,    // 用户点击了来源链接
    QueryRefined      // 用户细化了查询（追问）
}
