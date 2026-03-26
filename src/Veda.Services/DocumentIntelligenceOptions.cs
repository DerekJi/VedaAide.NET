namespace Veda.Services;

/// <summary>
/// Azure AI Document Intelligence 配置，绑定到 appsettings.json 的 "Veda:DocumentIntelligence" 节。
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    /// <summary>服务端点，例如 https://xxx.cognitiveservices.azure.com/。</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// API Key。留空时使用 Managed Identity（生产推荐）；
    /// 本地开发可填写 key 以跳过 Azure 登录。
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>是否已完整配置（端点非空即视为启用）。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}
