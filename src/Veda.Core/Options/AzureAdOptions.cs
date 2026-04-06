namespace Veda.Core.Options;

/// <summary>
/// Azure Entra ID (CIAM) 认证配置，绑定到 appsettings.json 的 "AzureAd" 节。
/// </summary>
public sealed class AzureAdOptions
{
    /// <summary>OIDC 授权服务器基地址，默认 https://login.microsoftonline.com/</summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>CIAM 域名，如 vedaaide.onmicrosoft.com。用于构造 OIDC 元数据 URL。</summary>
    public string? Domain { get; set; }

    /// <summary>Entra ID 租户 ID。</summary>
    public string? TenantId { get; set; }

    /// <summary>应用注册的 Client ID（Application ID）。</summary>
    public string? ClientId { get; set; }

    /// <summary>JWT audience，留空则使用 ClientId。</summary>
    public string? Audience { get; set; }

    /// <summary>管理员 OID 白名单（适合 CIAM token 无 roles claim 的场景）。</summary>
    public string[] AdminOids { get; set; } = [];
}
