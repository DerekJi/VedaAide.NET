using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Veda.Api.Middleware;

/// <summary>
/// Development-only authentication bypass handler.
/// Only active in the Development environment when AzureAd is not configured.
/// All requests pass authentication under the fixed "dev-user" identity without a JWT token.
/// ⚠ Must never be used in production.
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
