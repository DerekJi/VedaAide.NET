namespace Veda.Storage.Entities;

public class ChatSessionEntity
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string UserId    { get; set; } = string.Empty;
    public string Title     { get; set; } = "New Chat";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChatMessageEntity> Messages { get; set; } = [];
}

public class ChatMessageEntity
{
    public string MessageId         { get; set; } = Guid.NewGuid().ToString();
    public string SessionId         { get; set; } = string.Empty;
    public string UserId            { get; set; } = string.Empty;
    public string Role              { get; set; } = string.Empty;
    public string Content           { get; set; } = string.Empty;
    public float? Confidence        { get; set; }
    public bool   IsHallucination   { get; set; }
    public string SourcesJson       { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
