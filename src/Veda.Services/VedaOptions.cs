namespace Veda.Services;

/// <summary>
/// VedaAide 核心配置，绑定到 appsettings.json 的 "Veda" 节。
/// </summary>
public sealed class VedaOptions
{
    /// <summary>
    /// 当前使用的 Embedding 模型名称（如 nomic-embed-text、bge-m3、text-embedding-3-small）。
    /// 切换模型时，旧向量（EmbeddingModel 字段不匹配的行）需要重新摄取才能生效。
    /// </summary>
    public string EmbeddingModel { get; set; } = "bge-m3";

    /// <summary>
    /// Embedding 提供商："Ollama"（默认，本地）或 "AzureOpenAI"（云端）。
    /// </summary>
    public string EmbeddingProvider { get; set; } = "Ollama";

    /// <summary>
    /// LLM 提供商："Ollama"（默认，本地）或 "AzureOpenAI"（云端）。
    /// </summary>
    public string LlmProvider { get; set; } = "Ollama";
}
