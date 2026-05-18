using System.Security.Claims;
using DbConfig.Ui;

namespace PaymentsApi;

/// <summary>
/// Demo credential validator that checks the submitted password against
/// <c>Auth:Password</c> in <c>appsettings.json</c>. The username is treated
/// as a display label only. NOT FOR PRODUCTION — production hosts would
/// look up users in a database or identity provider and hash passwords.
/// </summary>
internal sealed class AppSettingsCredentialValidator(IConfiguration configuration) : IDbConfigCredentialValidator
{
    public Task<ClaimsPrincipal?> ValidateAsync(string username, string password, CancellationToken ct)
    {
        var expected = configuration["Auth:Password"];
        if (string.IsNullOrEmpty(expected) || !string.Equals(password, expected, StringComparison.Ordinal))
        {
            return Task.FromResult<ClaimsPrincipal?>(null);
        }

        var displayName = string.IsNullOrEmpty(username) ? "demo-admin" : username;
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, displayName)],
            "DbConfigCookie");

        return Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
    }
}
