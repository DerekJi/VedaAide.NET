using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Veda.Agents.Orchestration;

namespace Veda.Agents;

/// <summary>
/// LLM-driven Agent orchestration service using Microsoft Agent Framework.
/// Uses an AIAgent + Tool loop with autonomous reasoning (Reason-Act-Observe loop),
/// implementing IRCoT (Interleaved Retrieval + Chain-of-Thought).
/// </summary>
public sealed class LlmOrchestrationService(
    IChatClient                        chatClient,
    IEmbeddingService                  embeddingService,
    IVectorStore                       vectorStore,
    IDocumentIngestor                  documentIngestor,
    IHallucinationGuardService         hallucinationGuard,
    ILogger<LlmOrchestrationService>   logger) : IOrchestrationService
{
    private const string QueryAgentInstructions = """
        You are VedaAide, an intelligent knowledge-base assistant.
        When answering questions, follow these steps:
        1. ALWAYS call the search_knowledge_base tool first to retrieve relevant information.
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

        try
        {
            // Create tools for the agent
            var knowledgeBaseTool = new VedaKnowledgeBaseTool(embeddingService, vectorStore);
            var tools = new[]
            {
                AIFunctionFactory.Create(knowledgeBaseTool.SearchKnowledgeBase,
                    new AIFunctionFactoryOptions { Name = "search_knowledge_base" })
            };

            // Create the agent with MAF
            var agent = chatClient.AsAIAgent(
                instructions: QueryAgentInstructions,
                name: "QueryAgent",
                tools: tools);

            // Create a session for the conversation
            var session = await agent.CreateSessionAsync(ct);

            // Run the agent with the question
            var response = await agent.RunAsync(question, session, cancellationToken: ct);
            var answer = response.Text ?? "No answer could be generated.";

            // Extract tool call count from the messages
            int toolCallCount = response.Messages.Count(m => m.Role == ChatRole.Tool);
            if (toolCallCount > 0)
                trace.Add($"QueryAgent: invoked search_knowledge_base {toolCallCount} time(s)");
            else
                trace.Add("QueryAgent: completed without tool calls");

            answer = answer.Trim();
            if (string.IsNullOrEmpty(answer))
                answer = "No answer could be generated. Please try rephrasing your question.";

            trace.Add($"QueryAgent: final answer generated ({answer.Length} chars)");

            // Extract tool outputs as sources
            var toolSources = new List<SourceReference>();
            foreach (var msg in response.Messages.Where(m => m.Role == ChatRole.Tool))
            {
                if (!string.IsNullOrEmpty(msg.Text))
                {
                    toolSources.Add(new SourceReference
                    {
                        DocumentName = "knowledge-base",
                        ChunkContent = msg.Text.Length > 300
                            ? msg.Text[..300] + "…"
                            : msg.Text,
                        Similarity   = 0f
                    });
                }
            }

        // EvalAgent — context grounding check
        string? evalSummary = null;
        if (toolSources.Count > 0)
        {
            var context    = string.Join("\n\n", toolSources.Select(s => s.ChunkContent));
            var isGrounded = await hallucinationGuard.VerifyAsync(answer, context, ct);
            evalSummary    = isGrounded
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
    }

    public async Task<OrchestrationResult> RunIngestFlowAsync(
        string content, string documentName, CancellationToken ct = default)
    {
        // Ingest flow remains deterministic — no LLM reasoning needed for ingestion
        var trace = new List<string>();
        trace.Add($"DocumentAgent: analyzing document '{documentName}'");

        var docType = DocumentTypeParser.InferFromName(documentName);
        trace.Add($"DocumentAgent: inferred type = {docType}");

        var result = await documentIngestor.IngestAsync(content, documentName, docType, ct: ct);
        trace.Add($"DocumentAgent: stored {result.ChunksStored} chunks (documentId={result.DocumentId})");

        return new OrchestrationResult
        {
            Answer      = $"Document '{documentName}' ingested successfully: {result.ChunksStored} chunks stored.",
            IsEvaluated = false,
            AgentTrace  = trace.AsReadOnly()
        };
    }
}
