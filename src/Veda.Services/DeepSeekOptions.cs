namespace Veda.Services;

/// <summary>
/// DeepSeek（OpenAI 兼容）LLM 配置，绑定到 appsettings.json 的 "Veda:DeepSeek" 节。
/// </summary>
public sealed class DeepSeekOptions
{
    /// <summary>API 基础 URL，默认 deepseek.com 官方端点。</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";

    /// <summary>DeepSeek API Key；留空则系统自动降级到 Simple 模式。</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>模型名称，默认 deepseek-chat。</summary>
    public string ChatModel { get; set; } = "deepseek-chat";
}
