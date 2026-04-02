namespace Veda.Core.Interfaces;

public interface IChatSessionRepository
{
    Task<ChatSessionRecord> CreateAsync(string userId, string title, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSessionRecord>> ListAsync(string userId, CancellationToken ct = default);
    Task DeleteAsync(string sessionId, string userId, CancellationToken ct = default);

    Task AppendMessageAsync(ChatMessageRecord message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(string sessionId, string userId, CancellationToken ct = default);
}
