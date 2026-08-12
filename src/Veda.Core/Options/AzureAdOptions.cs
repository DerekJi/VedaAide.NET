namespace Veda.Core.Options;

/// <summary>
/// Azure Entra ID (CIAM) authentication configuration, bound to the "AzureAd" section of appsettings.json.
/// </summary>
public sealed class AzureAdOptions
{
    /// <summary>Base address of the OIDC authorization server, default https://login.microsoftonline.com/</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>CIAM domain name, e.g. vedaaide.onmicrosoft.com. Used to construct the OIDC metadata URL.</summary>
    public string? Domain { get; set; }

    /// <summary>Entra ID tenant ID.</summary>
    public string? TenantId { get; set; }

    /// <summary>The Client ID (Application ID) of the app registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>JWT audience; falls back to ClientId when left empty.</summary>
    public string? Audience { get; set; }

    /// <summary>Whitelist of administrator OIDs (for scenarios where CIAM tokens have no roles claim).</summary>
    public string[] AdminOids { get; set; } = [];
}
