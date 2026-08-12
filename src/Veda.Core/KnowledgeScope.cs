namespace Veda.Core;

/// <summary>
/// Knowledge scope metadata, used for multi-dimensional filtering and preference ranking.
/// All properties are optional — no filtering is applied when no scope is passed.
/// Visibility = null means no visibility-based filtering (for compatibility with historical data).
/// </summary>
public record KnowledgeScope(
    string? Domain = null,
    string? SourceType = null,
    IReadOnlyList<string>? Tags = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? OwnerId = null,
    Visibility? Visibility = null);

/// <summary>Knowledge visibility level.</summary>
public enum Visibility
{
    Private,   // visible to the owner only
    Shared,    // visible to authorized members
    Public     // visible to all users
}
