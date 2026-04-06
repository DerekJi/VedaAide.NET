namespace Veda.Core.Options;

/// <summary>
/// Azure CosmosDB 向量存储配置，绑定到 appsettings.json 的 "Veda:CosmosDb" 节。
/// </summary>
public sealed class CosmosDbOptions
{
    /// <summary>CosmosDB 账户端点，例如 https://xxx.documents.azure.com:443/</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>账户主键（留空则使用 DefaultAzureCredential / Managed Identity）</summary>
    public string? AccountKey { get; set; }

    /// <summary>数据库名称，默认 VedaAide</summary>
    public string DatabaseName { get; set; } = "VedaAide";

    /// <summary>向量块容器名称，默认 VectorChunks</summary>
    public string ChunksContainerName { get; set; } = "VectorChunks";

    /// <summary>语义缓存容器名称，默认 SemanticCache</summary>
    public string CacheContainerName { get; set; } = "SemanticCache";

    /// <summary>User behavior feedback container name, default UserBehaviors.</summary>
    public string BehaviorsContainerName { get; set; } = "UserBehaviors";

    /// <summary>Token usage log container name, default TokenUsages.</summary>
    public string TokenUsagesContainerName { get; set; } = "TokenUsages";

    /// <summary>Chat sessions container name, default ChatSessions.</summary>
    public string ChatSessionsContainerName { get; set; } = "ChatSessions";

    /// <summary>
    /// Embedding 向量维度。必须与实际使用的 Embedding 模型一致：
    /// bge-m3 = 1024，text-embedding-3-small = 1536。
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// 是否启用 Cosmos DB 原生全文检索（FullTextContainsAny + FullTextScore/BM25）用于关键词检索。
    /// 关闭时使用 CONTAINS + 本地改良 TF/覆盖率评分。
    /// </summary>
    public bool EnableFullTextKeywordSearch { get; set; }

    /// <summary>
    /// 全文检索语言代码（例如 en-US、zh-CN）。为空时由 Cosmos DB 使用默认语言配置。
    /// </summary>
    public string? FullTextLanguage { get; set; }
}
