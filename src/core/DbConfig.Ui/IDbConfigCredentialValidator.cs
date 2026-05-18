using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace DbConfig.Ui;

/// <summary>
/// Consumer-implemented validator for the built-in cookie login form
/// (enabled via <see cref="DbConfigUiOptions.UseBuiltInLogin{TValidator}"/>).
/// Register in DI as scoped — implementations may inject a DbContext or any
/// other scoped service for async credential lookups.
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
