using System.Security.Claims;

namespace DbConfig.Http;

/// <summary>
/// Consumer-implemented validator for the built-in cookie login form
/// (enabled via <c>opts.UseBuiltInLogin&lt;TValidator&gt;()</c> on
/// <c>MapDbConfigUi</c> / <c>MapDbConfigAdmin</c>). Register in DI as scoped
/// — implementations may inject a DbContext or any other scoped service for
/// async credential lookups.
/// </summary>
public interface IDbConfigCredentialValidator
{
    /// <summary>
    /// Validates the supplied credentials. Return <c>null</c> on failure;
    /// a populated <see cref="ClaimsPrincipal"/> on success. The returned
    /// principal's identity name is persisted in the signed auth cookie.
    /// </summary>
    Task<ClaimsPrincipal?> ValidateAsync(string username, string password, CancellationToken ct);
}
