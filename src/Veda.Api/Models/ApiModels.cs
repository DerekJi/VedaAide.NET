using System.ComponentModel.DataAnnotations;

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
    [Range(0.0, 1.0)] float MinSimilarity = 0.6f,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null);

public record SavePromptRequest(
    [Required, MinLength(1), MaxLength(200)] string Name,
    [Required, MinLength(1), MaxLength(50)]  string Version,
    [Required, MinLength(1)]                 string Content,
    int? DocumentType = null);
