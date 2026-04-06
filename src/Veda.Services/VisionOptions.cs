namespace Veda.Core.Options;

/// <summary>
/// Vision 模型提取配置，绑定到 appsettings.json 的 "Veda:Vision" 节。
/// </summary>
public sealed class VisionOptions
{
    /// <summary>
    /// Whether to enable the Vision extraction pipeline.
    /// Supported by AzureOpenAI (gpt-4o-mini) and multimodal Ollama models (e.g. qwen3-vl:8b).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Name of the dedicated Ollama vision model (e.g. qwen3-vl:8b).
    /// Leave empty to reuse the main ChatModel.
    /// Ignored when ModelProvider=AzureOpenAI.
    /// </summary>
    public string? OllamaModel { get; set; }

    /// <summary>
    /// Azure OpenAI chat deployment to use for Vision.
    /// Defaults to "gpt-4o-mini".
    /// </summary>
    public string ChatDeployment { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Ollama HTTP request timeout for vision inference, in seconds.
    /// Vision models (especially VL models running under VRAM pressure) can take longer than
    /// the default 100 s HttpClient timeout. Default: 300 s (5 minutes).
    /// Ignored when ModelProvider=AzureOpenAI.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;
}
