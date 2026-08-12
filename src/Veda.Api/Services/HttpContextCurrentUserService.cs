using Microsoft.Extensions.Configuration;

namespace Veda.Api.Services;

/// <summary>
/// Extracts the current user identity from HttpContext.User.
/// After Entra ID JWT Bearer validation, the oid claim is used as the UserId (object ID).
/// When Entra ID is not configured (anonymous access), UserId is always null.
/// IsAdmin is read primarily from the JWT roles claim (requires an App Role to be configured);
/// also supports an allowlist configured via AzureAd:AdminOids (for CIAM tokens without a roles claim).
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
