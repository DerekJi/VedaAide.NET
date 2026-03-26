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
