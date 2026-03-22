using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Veda.Agents.Orchestration;

namespace Veda.Agents;

/// <summary>
/// LLM 驱动的 Agent 编排服务。
/// 使用 Semantic Kernel <see cref="ChatCompletionAgent"/> + KernelFunction Plugin 循环，
/// LLM 自主决定何时调用 search_knowledge_base（Reason-Act-Observe 循环），
/// 实现 IRCoT (Interleaved Retrieval + Chain-of-Thought)。
/// </summary>
public sealed class LlmOrchestrationService(
    Kernel                             kernel,
    IEmbeddingService                  embeddingService,
    IVectorStore                       vectorStore,
    IDocumentIngestor                  documentIngestor,
    IHallucinationGuardService         hallucinationGuard,
    ILogger<LlmOrchestrationService>   logger) : IOrchestrationService
{
    private const string QueryAgentInstructions = """
        You are VedaAide, an intelligent knowledge-base assistant.
        When answering questions, follow these steps:
        1. ALWAYS call search_knowledge_base first to retrieve relevant information.
        2. If the initial results are insufficient, refine your search query and try again.
        3. Synthesize the retrieved information into a clear, accurate, concise answer.
        4. If the knowledge base contains no relevant information, say so explicitly.
        Think step by step before giving your final answer.
        """;

    public async Task<OrchestrationResult> RunQueryFlowAsync(
        string question, CancellationToken ct = default)
    {
        var trace = new List<string>();
        trace.Add("QueryAgent (LLM): starting agent loop with IRCoT");

        // Clone kernel so plugin registration is request-scoped
        var agentKernel = kernel.Clone();
        agentKernel.Plugins.AddFromObject(
            new VedaKernelPlugin(embeddingService, vectorStore),
            pluginName: "KnowledgeBase");

        var agent = new ChatCompletionAgent
        {
            Name         = "QueryAgent",
            Instructions = QueryAgentInstructions,
            Kernel       = agentKernel,
            Arguments    = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
        };

        var sb     = new System.Text.StringBuilder();
        var thread = new ChatHistoryAgentThread();

        try
        {
            await foreach (var item in agent.InvokeAsync(
                new ChatMessageContent(AuthorRole.User, question),
                thread: thread,
                cancellationToken: ct))
            {
                if (item.Message.Content is not null)
                    sb.Append(item.Message.Content);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM agent loop failed for question: {Question}", question);
            trace.Add($"QueryAgent: agent error — {ex.Message}");
            return new OrchestrationResult
            {
                Answer     = "An error occurred while processing your request. Please try again.",
                IsEvaluated = false,
                AgentTrace = trace.AsReadOnly()
            };
        }

        // Extract tool-call count and sources from conversation history
        var toolSources  = new List<SourceReference>();
        int toolCallCount = 0;

        foreach (var msg in thread.ChatHistory)
        {
            if (msg.Role == AuthorRole.Tool && msg.Content is { Length: > 0 })
            {
                toolCallCount++;
                toolSources.Add(new SourceReference
                {
                    DocumentName = "knowledge-base",
                    ChunkContent = msg.Content.Length > 300
                        ? msg.Content[..300] + "…"
                        : msg.Content,
                    Similarity   = 0f
                });
            }
        }

        if (toolCallCount > 0)
            trace.Add($"QueryAgent: invoked search_knowledge_base {toolCallCount} time(s)");
        else
            trace.Add("QueryAgent: completed without tool calls");

        var answer = sb.ToString().Trim();
        if (string.IsNullOrEmpty(answer))
            answer = "No answer could be generated. Please try rephrasing your question.";

        trace.Add($"QueryAgent: final answer generated ({answer.Length} chars)");

        // EvalAgent — context grounding check
        string? evalSummary = null;
        if (toolSources.Count > 0)
        {
            var context     = string.Join("\n\n", toolSources.Select(s => s.ChunkContent));
            var isGrounded  = await hallucinationGuard.VerifyAsync(answer, context, ct);
            evalSummary     = isGrounded
                ? "EvalAgent: answer is grounded in source documents ✓"
                : "EvalAgent: answer may not be fully supported by retrieved context ⚠";
            trace.Add(evalSummary);
        }
        else
        {
            trace.Add("EvalAgent: skipped (no sources retrieved)");
        }

        return new OrchestrationResult
        {
            Answer            = answer,
            IsEvaluated       = evalSummary is not null,
            EvaluationSummary = evalSummary,
            AgentTrace        = trace.AsReadOnly()
        };
    }

    public async Task<OrchestrationResult> RunIngestFlowAsync(
        string content, string documentName, CancellationToken ct = default)
    {
        // Ingest flow remains deterministic — no LLM reasoning needed for ingestion
        var trace = new List<string>();
        trace.Add($"DocumentAgent: analyzing document '{documentName}'");

        var docType = DocumentTypeParser.InferFromName(documentName);
        trace.Add($"DocumentAgent: inferred type = {docType}");

        var result = await documentIngestor.IngestAsync(content, documentName, docType, ct);
        trace.Add($"DocumentAgent: stored {result.ChunksStored} chunks (documentId={result.DocumentId})");

        return new OrchestrationResult
        {
            Answer      = $"Document '{documentName}' ingested successfully: {result.ChunksStored} chunks stored.",
            IsEvaluated = false,
            AgentTrace  = trace.AsReadOnly()
        };
    }
}
