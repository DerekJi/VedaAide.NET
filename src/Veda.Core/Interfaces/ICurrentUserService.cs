namespace Veda.Core.Interfaces;

/// <summary>
/// 当前已认证用户的身份服务。
/// 五期前：匿名访问，UserId = null。
/// 五期后（B2C）：JWT Bearer 中间件验证后从 Token claims 提取。
/// </summary>
public interface ICurrentUserService
{
    /// <summary>用户唯一 ID（来自 JWT oid/sub claim）。null = 未登录 / 匿名。</summary>
    string? UserId { get; }

    /// <summary>是否已通过身份验证。</summary>
    bool IsAuthenticated { get; }

    /// <summary>是否拥有管理员角色（JWT roles claim 包含 "Admin"）。</summary>
    bool IsAdmin { get; }
}
