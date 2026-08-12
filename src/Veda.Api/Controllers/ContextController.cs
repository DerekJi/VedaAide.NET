using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Veda.Services;

namespace Veda.Api.Controllers;

/// <summary>
/// Context Augmentation endpoints: extract text from user-uploaded files without writing to the database (Ephemeral RAG).
/// </summary>
[ApiController]
[Route("api/context")]
[Authorize]
public class ContextController(
    EphemeralContextExtractor extractor,
    ILogger<ContextController> logger) : ControllerBase
{
    /// <summary>
    /// Extracts text content from a file, returning it to the frontend only, without writing to the vector store.
    /// </summary>
    [HttpPost("extract")]
    [RequestSizeLimit(20 * 1024 * 1024)]  // 20 MB limit
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Extract(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var mimeType = file.ContentType ?? MediaTypeNames.Application.Octet;

        logger.LogInformation(
            "Context extract request: '{Name}' ({Mime}, {Bytes} bytes)",
            file.FileName, mimeType, file.Length);

        await using var stream = file.OpenReadStream();
        var text = await extractor.ExtractAsync(stream, file.FileName, mimeType, ct);

        if (text is null)
        {
            return UnprocessableEntity(new
            {
                error = "Could not extract text from the uploaded file. " +
                        "Ensure Vision is enabled for images, or try a text-based PDF."
            });
        }

        return Ok(new { text, fileName = file.FileName });
    }
}
