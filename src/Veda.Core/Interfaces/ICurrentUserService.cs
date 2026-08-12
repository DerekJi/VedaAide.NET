namespace Veda.Core.Interfaces;

/// <summary>
/// Identity service for the currently authenticated user.
/// Before phase 5: anonymous access, UserId = null.
/// From phase 5 onward (B2C): extracted from token claims after JWT Bearer middleware validation.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Unique user ID (from the JWT oid/sub claim). null = not signed in / anonymous.</summary>
    string? UserId { get; }

    /// <summary>Whether the user has been authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Whether the user holds the admin role (the JWT roles claim contains "Admin").</summary>
    bool IsAdmin { get; }
}
