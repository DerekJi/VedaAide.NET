namespace Veda.Agents.Orchestration;

/// <summary>
/// Agent 编排服务接口：协调 DocumentAgent / QueryAgent / EvalAgent 完成复杂多步任务。
/// </summary>
public interface IOrchestrationService
{
    /// <summary>
    /// 执行智能问答流程：QueryAgent 检索 + 生成，EvalAgent 质量评估。
    /// </summary>
    Task<OrchestrationResult> RunQueryFlowAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// 执行文档摄取流程：DocumentAgent 决策 + 摄取，返回摄取摘要。
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
