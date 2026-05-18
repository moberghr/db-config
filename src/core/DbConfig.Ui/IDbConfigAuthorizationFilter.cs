using Microsoft.AspNetCore.Http;

namespace DbConfig.Ui;

/// <summary>
/// General per-request authorization filter for the DbConfig admin surface.
/// Use when a credential form doesn't fit (e.g., header-based auth, IP
/// allowlists, custom JWT validation). For username/password flows prefer
/// <see cref="IDbConfigCredentialValidator"/> +
/// <see cref="DbConfigUiOptions.UseBuiltInLogin{TValidator}"/>.
/// </summary>
public interface IDbConfigAuthorizationFilter
{
    /// <summary>
    /// Returns <c>true</c> when the request is allowed to access the UI route group;
    /// <c>false</c> otherwise. Should not block on I/O for hot-path performance.
    /// </summary>
    Task<bool> IsAuthorizedAsync(HttpContext context);
}
