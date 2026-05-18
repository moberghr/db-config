using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PaymentsApi;

// Static API-key auth handler for the admin surface only (NOT FOR PROD).
internal sealed class ApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue("X-Admin-Api-Key", out var provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = configuration["Auth:Password"];
        if (string.IsNullOrEmpty(expected) || !string.Equals(provided, expected, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "demo-admin")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }
}
