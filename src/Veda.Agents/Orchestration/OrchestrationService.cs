namespace Veda.Agents.Orchestration;

/// <summary>
/// 手动调用链编排（比 AgentGroupChat 更可控、更易测试）。
/// 流程：QueryAgent 生成答案 → EvalAgent 评估质量 → 返回结果。
/// </summary>
public sealed class OrchestrationService(
    IQueryService queryService,
    IDocumentIngestor documentIngestor,
    IHallucinationGuardService hallucinationGuard) : IOrchestrationService
{
    public async Task<OrchestrationResult> RunQueryFlowAsync(string question, CancellationToken ct = default)
    {
        var trace = new List<string>();

        // Step 1: QueryAgent — RAG 检索 + LLM 生成
        trace.Add("QueryAgent: executing RAG pipeline");
        var request = new RagQueryRequest { Question = question };
        var response = await queryService.QueryAsync(request, ct);
        trace.Add($"QueryAgent: answer generated (confidence={response.AnswerConfidence:P0}, hallucination={response.IsHallucination})");

        // Step 2: EvalAgent — 自动评估（若答案非幻觉则验证上下文一致性）
        string? evalSummary = null;
        if (!response.IsHallucination && response.Sources.Count > 0)
        {
            var context = string.Join("\n\n", response.Sources.Select(s => s.ChunkContent));
            var isGrounded = await hallucinationGuard.VerifyAsync(response.Answer, context, ct);
            evalSummary = isGrounded
                ? "EvalAgent: answer is grounded in source documents ✓"
                : "EvalAgent: answer may not be fully supported by retrieved context ⚠";
            trace.Add(evalSummary);
        }
        else
        {
            trace.Add("EvalAgent: skipped (hallucination detected or no sources)");
        }

        return new OrchestrationResult
        {
            Answer = response.Answer,
            IsEvaluated = evalSummary is not null,
            EvaluationSummary = evalSummary,
            AgentTrace = trace.AsReadOnly()
        };
    }

    public async Task<OrchestrationResult> RunIngestFlowAsync(
        string content, string documentName, CancellationToken ct = default)
    {
        var trace = new List<string>();

        // DocumentAgent — 决策文档类型并摄取
        trace.Add($"DocumentAgent: analyzing document '{documentName}'");
        var docType = InferDocumentType(documentName);
        trace.Add($"DocumentAgent: inferred type = {docType}");

        var result = await documentIngestor.IngestAsync(content, documentName, docType, ct: ct);
        trace.Add($"DocumentAgent: stored {result.ChunksStored} chunks (documentId={result.DocumentId})");

        return new OrchestrationResult
        {
            Answer = $"Document '{documentName}' ingested successfully: {result.ChunksStored} chunks stored.",
            IsEvaluated = false,
            AgentTrace = trace.AsReadOnly()
        };
    }

    // SRP: 文档类型推断逻辑独立于 Service 层
    private static DocumentType InferDocumentType(string documentName)
    {
        var name = documentName.ToLowerInvariant();
        if (name.Contains("invoice") || name.Contains("bill") || name.Contains("receipt"))
            return DocumentType.BillInvoice;
        if (name.Contains("spec") || name.Contains("pds") || name.Contains("requirement"))
            return DocumentType.Specification;
        if (name.Contains("report") || name.Contains("summary"))
            return DocumentType.Report;
        return DocumentType.Other;
    }
}
