using System.Runtime.CompilerServices;

namespace Veda.Services;

/// <summary>
/// 公开简历定制服务实现。
/// 流程：JD 向量化 → 检索 Public 简历片段 → 构建 Prompt → LLM 流式生成 Markdown 简历。
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
        // 1. 向量化 JD
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(jobDescription, ct);

        // 2. 检索 Visibility=Public 的简历片段（无 OwnerId 过滤，仅公开内容）
        var results = await vectorStore.SearchAsync(
            queryEmbedding,
            topK: topK,
            minSimilarity: 0.2f,       // 简历素材相似度阈值可以低一些，保证召回率
            scope: new KnowledgeScope(Visibility: Visibility.Public),
            ct: ct);

        // 3. 构建上下文
        var context = results.Count > 0
            ? string.Join("\n\n---\n\n", results.Select(r => r.Chunk.Content))
            : string.Empty;

        // 4. 构建用户消息
        var userMessage = string.IsNullOrWhiteSpace(context)
            ? $"Job Description:\n{jobDescription}\n\nNote: No specific candidate profile was found. Generate a general professional resume structure."
            : $"""
              Candidate Profile (use ONLY this information):
              {context}

              Job Description:
              {jobDescription}

              Generate a tailored Markdown resume for this candidate that highlights the most relevant experience and skills for this role.
              """;

        // 5. 流式调用 LLM
        var chatService = llmRouter.Resolve(QueryMode.Simple);
        await foreach (var token in chatService.CompleteStreamAsync(SystemPrompt, userMessage, ct))
        {
            yield return token;
        }
    }
}
