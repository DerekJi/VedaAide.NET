using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/prompts")]
public class PromptsController(IPromptTemplateRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await repository.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SavePromptRequest request, CancellationToken ct)
    {
        var template = new PromptTemplate
        {
            Name    = request.Name,
            Version = request.Version,
            Content = request.Content,
            DocumentType = request.DocumentType.HasValue
                ? (DocumentType)request.DocumentType.Value
                : null
        };

        await repository.SaveAsync(template, ct);
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await repository.DeleteAsync(id, ct);
        return NoContent();
    }
}
