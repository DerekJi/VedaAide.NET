using System.Runtime.CompilerServices;

namespace Veda.Services;

/// <summary>
/// Public resume tailoring service implementation.
/// Pipeline: embed the JD → retrieve Public resume snippets → build the prompt → stream-generate a Markdown resume via LLM.
/// </summary>
public sealed class PublicResumeTailoringService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    ILlmRouter llmRouter) : IPublicResumeTailoringService
{
    private const string SystemPrompt = """
        You are a professional resume writer. Your task is to generate a tailored Markdown resume for the candidate based on their background and the provided job description.

        Rules:
        1. ONLY use information from the provided candidate profile. Do NOT invent, assume, or add any facts not present in the context.
        2. Highlight skills, experiences, and achievements that are most relevant to the job description.
        3. Output a clean, well-structured Markdown document with clear headings (##, ###).
        4. Keep the tone professional and concise.
        5. Do NOT include phone numbers or home addresses.
        6. If the job description is in Chinese, respond in Chinese. Otherwise respond in English.
        7. Output raw Markdown ONLY. Do NOT wrap the output in a code fence (no ```markdown or ``` blocks).
        """;

    public async IAsyncEnumerable<string> TailorStreamAsync(
        string jobDescription,
        int topK,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Embed the JD
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(jobDescription, ct);

        // 2. Retrieve resume snippets with Visibility=Public (no OwnerId filter, public content only)
        var results = await vectorStore.SearchAsync(
            queryEmbedding,
            topK: topK,
            minSimilarity: 0.2f,       // Resume-material similarity threshold can be lower to ensure recall
            scope: new KnowledgeScope(Visibility: Visibility.Public),
            ct: ct);

        // 3. Build the context
        var context = results.Count > 0
            ? string.Join("\n\n---\n\n", results.Select(r => r.Chunk.Content))
            : string.Empty;

        // 4. Build the user message
        var userMessage = string.IsNullOrWhiteSpace(context)
            ? $"Job Description:\n{jobDescription}\n\nNote: No specific candidate profile was found. Generate a general professional resume structure."
            : $"""
              Candidate Profile (use ONLY this information):
              {context}

              Job Description:
              {jobDescription}

              Generate a tailored Markdown resume for this candidate that highlights the most relevant experience and skills for this role.
              """;

        // 5. Call the LLM in streaming mode
        var chatService = llmRouter.Resolve(QueryMode.Simple);
        await foreach (var token in chatService.CompleteStreamAsync(SystemPrompt, userMessage, ct))
        {
            yield return token;
        }
    }
}
