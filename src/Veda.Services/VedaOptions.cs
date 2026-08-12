namespace Veda.Core.Options;

/// <summary>
/// Core VedaAide configuration, bound to the "Veda" section of appsettings.json.
/// </summary>
public sealed class VedaOptions
{
    /// <summary>The Embedding model name currently in use (e.g. nomic-embed-text, bge-m3, text-embedding-3-small).</summary>
    public string EmbeddingModel { get; set; } = "bge-m3";

    /// <summary>Embedding provider: "Ollama" (default, local) or "AzureOpenAI" (cloud).</summary>
    public string EmbeddingProvider { get; set; } = "Ollama";

    /// <summary>LLM provider: "Ollama" (default, local) or "AzureOpenAI" (cloud).</summary>
    public string LlmProvider { get; set; } = "Ollama";

    /// <summary>Storage provider: "Sqlite" (default, local development) or "CosmosDb" (cloud deployment).</summary>
    public string StorageProvider { get; set; } = "Sqlite";

    /// <summary>Ollama service endpoint (including port), e.g. http://localhost:11434.</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    /// <summary>Ollama chat model name, e.g. qwen3:8b.</summary>
    public string ChatModel { get; set; } = "qwen3:8b";

    /// <summary>SQLite database file path.</summary>
    public string DbPath { get; set; } = "veda.db";

    /// <summary>Azure OpenAI settings, bound to the "Veda:AzureOpenAI" section.</summary>
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();

    /// <summary>Vision model settings, bound to the "Veda:Vision" section.</summary>
    public VisionOptions Vision { get; set; } = new();

    /// <summary>DeepSeek model settings, bound to the "Veda:DeepSeek" section.</summary>
    public DeepSeekSettings DeepSeek { get; set; } = new();

    /// <summary>API Key security settings, bound to the "Veda:Security" section.</summary>
    public SecuritySettings Security { get; set; } = new();

    /// <summary>Public resume generation endpoint settings, bound to the "Veda:PublicResume" section.</summary>
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

    /// <summary>Settings for the public resume generation endpoint (/api/public/resume/tailor).</summary>
    public sealed class PublicResumeSettings
    {
        /// <summary>Maximum requests per IP per hour (recommended 5 in production; can be relaxed to 30 for local development).</summary>
        public int RateLimitPerIpPerHour    { get; set; } = 5;
        /// <summary>Maximum character count for the JD text, to prevent overlong-Prompt attacks.</summary>
        public int MaxJobDescriptionChars   { get; set; } = 4000;
        /// <summary>Maximum number of resume snippets returned by vector retrieval.</summary>
        public int DefaultTopK              { get; set; } = 8;
    }
}

