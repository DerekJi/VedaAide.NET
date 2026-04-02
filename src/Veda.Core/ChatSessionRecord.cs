namespace Veda.Core;

public record ChatSessionRecord(
    string SessionId,
    string UserId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record ChatMessageRecord(
    string MessageId,
    string SessionId,
    string UserId,
    string Role,
    string Content,
    float? Confidence,
    bool IsHallucination,
    IReadOnlyList<ChatSourceRef> Sources,
    DateTimeOffset CreatedAt
);

public record ChatSourceRef(
    string DocumentName,
    string ChunkContent,
    float Similarity,
    string? ChunkId,
    string? DocumentId
);
