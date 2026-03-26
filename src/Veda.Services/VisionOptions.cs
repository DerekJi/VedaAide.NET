namespace Veda.Services;

/// <summary>
/// Vision 模型提取配置，绑定到 appsettings.json 的 "Veda:Vision" 节。
/// 仅对 <see cref="DocumentType.RichMedia"/> 类型的文件启用。
/// </summary>
public sealed class VisionOptions
{
    /// <summary>
    /// 是否启用 Vision 提取通道。
    /// AzureOpenAI 环境下建议 true；Ollama 本地开发保持 false（Ollama 不支持图片输入）。
    /// </summary>
    public bool Enabled { get; set; } = false;
}
