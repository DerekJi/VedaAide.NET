using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Veda.Api.Middleware;

/// <summary>
/// 开发专用认证绕过处理器。
/// 仅在 Development 环境且未配置 AzureAd 时激活。
/// 所有请求均以固定的 "dev-user" 身份通过认证，无需 JWT Token。
/// ⚠ 生产环境绝对禁止使用此处理器。
/// </summary>
public sealed class DevBypassAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory                               logger,
    UrlEncoder                                   encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevBypass";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims  = new[] { new Claim("oid", "dev-user"), new Claim("name", "Dev User") };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
