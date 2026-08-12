namespace Veda.Core;

/// <summary>A user behavior event, recording a user's interaction with the RAG system.</summary>
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

/// <summary>User behavior type.</summary>
public enum BehaviorType
{
    ResultAccepted,   // the user accepted the recommended result
    ResultRejected,   // the user marked the result as irrelevant
    AnswerEdited,     // the user edited the AI output
    SourceClicked,    // the user clicked a source link
    QueryRefined      // the user refined the query (follow-up)
}
