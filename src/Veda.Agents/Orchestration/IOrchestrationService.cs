namespace Veda.Agents.Orchestration;

/// <summary>
/// Agent orchestration service interface: coordinates DocumentAgent / QueryAgent / EvalAgent to complete complex multi-step tasks.
/// </summary>
public interface IOrchestrationService
{
    /// <summary>
    /// Runs the intelligent Q&A flow: QueryAgent retrieves + generates, EvalAgent assesses quality.
    /// </summary>
    Task<OrchestrationResult> RunQueryFlowAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// Runs the document ingestion flow: DocumentAgent decides + ingests, returning an ingestion summary.
    /// </summary>
    Task<OrchestrationResult> RunIngestFlowAsync(string content, string documentName, CancellationToken ct = default);
}

public record OrchestrationResult
{
    public required string Answer { get; init; }
    public bool IsEvaluated { get; init; }
    public string? EvaluationSummary { get; init; }
    public IReadOnlyList<string> AgentTrace { get; init; } = [];
}
