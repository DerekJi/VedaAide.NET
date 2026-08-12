namespace Veda.Core.Options;

/// <summary>
/// DeepSeek (OpenAI-compatible) LLM configuration, bound to the "Veda:DeepSeek" section of appsettings.json.
/// </summary>
public sealed class DeepSeekOptions
{
    /// <summary>API base URL; defaults to the official deepseek.com endpoint.</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";

    /// <summary>DeepSeek API key; if left empty the system automatically falls back to Simple mode.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model name; defaults to deepseek-chat.</summary>
    public string ChatModel { get; set; } = "deepseek-chat";
}
