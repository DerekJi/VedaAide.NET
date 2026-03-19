namespace Veda.Services;

/// <summary>
/// VedaAide 核心配置，绑定到 appsettings.json 的 "Veda" 节。
/// </summary>
public sealed class VedaOptions
{
    /// <summary>
    /// 当前使用的 Embedding 模型名称（如 nomic-embed-text、mxbai-embed-large）。
    /// 切换模型时，旧向量（EmbeddingModel 字段不匹配的行）需要重新摄取才能生效。
    /// </summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}
