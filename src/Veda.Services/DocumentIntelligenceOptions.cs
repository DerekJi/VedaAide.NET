namespace Veda.Core.Options;

/// <summary>
/// Azure AI Document Intelligence configuration, bound to the "Veda:DocumentIntelligence" section of appsettings.json.
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    /// <summary>Service endpoint, e.g. https://xxx.cognitiveservices.azure.com/.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// API key. When left empty, Managed Identity is used (recommended for production);
    /// for local development you can fill in the key to skip Azure sign-in.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Whether the configuration is complete (a non-empty endpoint counts as enabled).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}
