using Microsoft.Extensions.Configuration;

namespace Veda.Api.Services;

/// <summary>
/// 从 HttpContext.User 提取当前用户身份。
/// Entra ID JWT Bearer 验证后，oid claim 用作 UserId（对象 ID）。
/// 未配置 Entra ID 时（匿名访问），UserId 始终为 null。
/// IsAdmin 优先从 JWT roles claim 读取（需配置 App Role）；
/// 同时支持通过 AzureAd:AdminOids 配置白名单（适合 CIAM token 无 roles claim 的场景）。
/// </summary>
public sealed class HttpContextCurrentUserService(
    IHttpContextAccessor accessor,
    IConfiguration configuration)
    : ICurrentUserService
{
    // Evaluated once per service instance (Scoped); config does not change at runtime.
    private readonly IReadOnlySet<string> AdminOids =
        new HashSet<string>(
            configuration.GetSection("AzureAd:AdminOids").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);

    public string? UserId =>
        // CIAM access tokens may contain 'oid' or 'sub' (sometimes both).
        // With MapInboundClaims=false claims stay verbatim; keep ClaimTypes fallback
        // for any configuration that still uses the default remapping.
        accessor.HttpContext?.User.FindFirst("oid")?.Value
        ?? accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? accessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    public bool IsAuthenticated =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsAdmin =>
        // Accept either JWT App Role or OID whitelist (CIAM tokens may not carry roles).
        accessor.HttpContext?.User.IsInRole("Admin") == true
        || (UserId is not null && AdminOids.Contains(UserId));
}
