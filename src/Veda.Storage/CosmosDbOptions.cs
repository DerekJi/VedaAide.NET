namespace Veda.Storage;

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

    /// <summary>
    /// Embedding 向量维度。必须与实际使用的 Embedding 模型一致：
    /// bge-m3 = 1024，text-embedding-3-small = 1536。
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1024;
}
