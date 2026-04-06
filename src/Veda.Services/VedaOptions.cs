namespace Veda.Core.Options;

/// <summary>
/// VedaAide 核心配置，绑定到 appsettings.json 的 "Veda" 节。
/// </summary>
public sealed class VedaOptions
{
    /// <summary>当前使用的 Embedding 模型名称（如 nomic-embed-text、bge-m3、text-embedding-3-small）。</summary>
    public string EmbeddingModel { get; set; } = "bge-m3";

    /// <summary>Embedding 提供商："Ollama"（默认，本地）或 "AzureOpenAI"（云端）。</summary>
    public string EmbeddingProvider { get; set; } = "Ollama";

    /// <summary>LLM 提供商："Ollama"（默认，本地）或 "AzureOpenAI"（云端）。</summary>
    public string LlmProvider { get; set; } = "Ollama";

    /// <summary>存储提供商："Sqlite"（默认，本地开发）或 "CosmosDb"（云端部署）。</summary>
    public string StorageProvider { get; set; } = "Sqlite";

    /// <summary>Ollama 服务端点（含端口），如 http://localhost:11434。</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    /// <summary>Ollama 对话模型名称，如 qwen3:8b。</summary>
    public string ChatModel { get; set; } = "qwen3:8b";

    /// <summary>SQLite 数据库文件路径。</summary>
    public string DbPath { get; set; } = "veda.db";

    /// <summary>Azure OpenAI 相关配置，绑定到 "Veda:AzureOpenAI" 节。</summary>
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();

    /// <summary>Vision 模型配置，绑定到 "Veda:Vision" 节。</summary>
    public VisionOptions Vision { get; set; } = new();

    /// <summary>DeepSeek 模型配置，绑定到 "Veda:DeepSeek" 节。</summary>
    public DeepSeekSettings DeepSeek { get; set; } = new();

    /// <summary>API Key 安全配置，绑定到 "Veda:Security" 节。</summary>
    public SecuritySettings Security { get; set; } = new();

    /// <summary>公开简历生成端点配置，绑定到 "Veda:PublicResume" 节。</summary>
    public PublicResumeSettings PublicResume { get; set; } = new();

    // ── Nested settings classes ───────────────────────────────────────────────

    public sealed class AzureOpenAISettings
    {
        public string? Endpoint            { get; set; }
        public string? ApiKey              { get; set; }
        public string  EmbeddingDeployment { get; set; } = "text-embedding-3-small";
        public string  ChatDeployment      { get; set; } = "gpt-4o-mini";
    }

    public sealed class DeepSeekSettings
    {
        public string  BaseUrl   { get; set; } = "https://api.deepseek.com/v1";
        public string? ApiKey    { get; set; }
        public string  ChatModel { get; set; } = "deepseek-chat";
    }

    public sealed class SecuritySettings
    {
        public string? ApiKey          { get; set; }
        public string? AdminApiKey     { get; set; }
        public string  AllowedOrigins  { get; set; } = "*";
    }

    /// <summary>公开简历生成端点（/api/public/resume/tailor）的配置。</summary>
    public sealed class PublicResumeSettings
    {
        /// <summary>每 IP 每小时最大请求次数（生产建议 5，本地开发可放宽到 30）。</summary>
        public int RateLimitPerIpPerHour    { get; set; } = 5;
        /// <summary>JD 文本最大字符数，防止超长 Prompt 攻击。</summary>
        public int MaxJobDescriptionChars   { get; set; } = 4000;
        /// <summary>向量检索返回的最大简历片段数。</summary>
        public int DefaultTopK              { get; set; } = 8;
    }
}

