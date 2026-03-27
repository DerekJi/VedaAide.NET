namespace Veda.Api.Services;

/// <summary>
/// 从 HttpContext.User 提取当前用户身份。
/// Entra ID JWT Bearer 验证后，oid claim 用作 UserId（对象 ID）。
/// 未配置 Entra ID 时（匿名访问），UserId 始终为 null。
/// </summary>
public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor)
    : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User.FindFirst("oid")?.Value
        ?? accessor.HttpContext?.User.FindFirst("sub")?.Value;

    public bool IsAuthenticated =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
