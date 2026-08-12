namespace Veda.Core.Options;

/// <summary>
/// Azure CosmosDB vector store configuration, bound to the "Veda:CosmosDb" section of appsettings.json.
/// </summary>
public sealed class CosmosDbOptions
{
    /// <summary>CosmosDB account endpoint, e.g. https://xxx.documents.azure.com:443/</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Account primary key (leave empty to use DefaultAzureCredential / Managed Identity)</summary>
    public string? AccountKey { get; set; }

    /// <summary>Database name, default VedaAide</summary>
    public string DatabaseName { get; set; } = "VedaAide";

    /// <summary>Vector chunks container name, default VectorChunks</summary>
    public string ChunksContainerName { get; set; } = "VectorChunks";

    /// <summary>Semantic cache container name, default SemanticCache</summary>
    public string CacheContainerName { get; set; } = "SemanticCache";

    /// <summary>User behavior feedback container name, default UserBehaviors.</summary>
    public string BehaviorsContainerName { get; set; } = "UserBehaviors";

    /// <summary>Token usage log container name, default TokenUsages.</summary>
    public string TokenUsagesContainerName { get; set; } = "TokenUsages";

    /// <summary>Chat sessions container name, default ChatSessions.</summary>
    public string ChatSessionsContainerName { get; set; } = "ChatSessions";

    /// <summary>
    /// Embedding vector dimensions. Must match the Embedding model actually in use:
    /// bge-m3 = 1024, text-embedding-3-small = 1536.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// Whether to enable Cosmos DB native full-text search (FullTextContainsAny + FullTextScore/BM25) for keyword retrieval.
    /// When disabled, uses CONTAINS plus a locally improved TF/coverage scoring.
    /// </summary>
    public bool EnableFullTextKeywordSearch { get; set; }

    /// <summary>
    /// Full-text search language code (e.g. en-US, zh-CN). When empty, Cosmos DB uses its default language configuration.
    /// </summary>
    public string? FullTextLanguage { get; set; }
}
