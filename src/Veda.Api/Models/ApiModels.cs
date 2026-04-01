using System.ComponentModel.DataAnnotations;
using Veda.Core;

namespace Veda.Api.Models;

internal static class ApiConstraints
{
    internal const int DocumentNameMaxLength = 500;
    internal const int TopKMin = 1;
    internal const int TopKMax = 20;
}

public record IngestRequest(
    [Required, MinLength(1)] string Content,
    [Required, MinLength(1), MaxLength(ApiConstraints.DocumentNameMaxLength)] string DocumentName,
    string? DocumentType);

public record QueryRequest(
    [Required, MinLength(1)] string Question,
    string? DocumentType,
    [Range(ApiConstraints.TopKMin, ApiConstraints.TopKMax)] int TopK = 5,
    [Range(0.0, 1.0)] float MinSimilarity = RagDefaults.DefaultMinSimilarity,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    QueryMode Mode = QueryMode.Simple,
    bool StructuredOutput = false,
    string? ScopeDomain = null,
    string? ScopeOwnerId = null,
    string? UserId = null);

// ── Chat session API models ───────────────────────────────────────────────────

public record CreateSessionRequest(string? Title);

public record AppendMessageRequest(
    [Required] string Role,
    [Required, MinLength(1), MaxLength(10_000)] string Content,
    float? Confidence,
    bool IsHallucination,
    IReadOnlyList<ChatSourceRefDto>? Sources);

public record ChatSourceRefDto(
    string DocumentName,
    string ChunkContent,
    float Similarity,
    string? ChunkId,
    string? DocumentId);

public record SessionResponse(
    string SessionId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record MessageResponse(
    string MessageId,
    string SessionId,
    string Role,
    string Content,
    float? Confidence,
    bool IsHallucination,
    IReadOnlyList<ChatSourceRefDto> Sources,
    DateTimeOffset CreatedAt);

public record SavePromptRequest(
    [Required, MinLength(1), MaxLength(200)] string Name,
    [Required, MinLength(1), MaxLength(50)]  string Version,
    [Required, MinLength(1)]                 string Content,
    int? DocumentType = null);

public record SaveEvalQuestionRequest(
    [Required, MinLength(1)] string Question,
    [Required, MinLength(1)] string ExpectedAnswer,
    string[]? Tags = null);

public record RunEvaluationRequest(
    string[]? QuestionIds       = null,
    string?   ChatModelOverride = null);
