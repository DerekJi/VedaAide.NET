using System.ComponentModel.DataAnnotations;
using Veda.Core.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/evaluation")]
public class EvaluationController(
    IEvalDatasetRepository datasetRepo,
    IEvalReportRepository  reportRepo,
    IEvaluationRunner      runner) : ControllerBase
{
    // ── Golden Dataset ─────────────────────────────────────────────────────────

    [HttpGet("questions")]
    public async Task<IActionResult> ListQuestions(CancellationToken ct)
    {
        var questions = await datasetRepo.ListAsync(ct);
        return Ok(questions);
    }

    [HttpPost("questions")]
    public async Task<IActionResult> SaveQuestion([FromBody] SaveEvalQuestionRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var question = new EvalQuestion
        {
            Question       = req.Question,
            ExpectedAnswer = req.ExpectedAnswer,
            Tags           = req.Tags ?? [],
        };

        var saved = await datasetRepo.SaveAsync(question, ct);
        return Created($"api/evaluation/questions/{saved.Id}", saved);
    }

    [HttpDelete("questions/{id}")]
    public async Task<IActionResult> DeleteQuestion(string id, CancellationToken ct)
    {
        await datasetRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    // ── Evaluation Runs ────────────────────────────────────────────────────────

    [HttpPost("run")]
    public async Task<IActionResult> RunEvaluation([FromBody] RunEvaluationRequest req, CancellationToken ct)
    {
        var options = new EvalRunOptions
        {
            QuestionIds       = req.QuestionIds ?? [],
            ChatModelOverride = req.ChatModelOverride,
            DatasetSource     = req.DatasetSource ?? EvalDatasetSource.Database,
            DatasetConfig     = req.DatasetConfig,
        };

        try
        {
            var report = await runner.RunAsync(options, ct);
            await reportRepo.SaveAsync(report, ct);
            return Ok(report);
        }
        // No registered IEvalDatasetProvider supports the requested source (e.g. HuggingFace/LocalFile
        // are valid enum values but their providers are not implemented yet) — surface it as a client
        // error instead of a generic 500. We catch only the domain exception and return a sanitized
        // ProblemDetails (no internal type names or DI details) rather than echoing the raw message.
        catch (UnsupportedEvalDatasetSourceException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title  = "Unsupported dataset source",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    // ── Reports ────────────────────────────────────────────────────────────────

    [HttpGet("reports")]
    public async Task<IActionResult> ListReports(
        [FromQuery, Range(1, 200)] int limit = 20, CancellationToken ct = default)
    {
        var reports = await reportRepo.ListAsync(limit, ct);
        return Ok(reports);
    }

    [HttpGet("reports/{runId}")]
    public async Task<IActionResult> GetReport(string runId, CancellationToken ct)
    {
        var report = await reportRepo.GetAsync(runId, ct);
        if (report is null)
            return NotFound();
        return Ok(report);
    }

    [HttpDelete("reports/{runId}")]
    public async Task<IActionResult> DeleteReport(string runId, CancellationToken ct)
    {
        await reportRepo.DeleteAsync(runId, ct);
        return NoContent();
    }
}
