using DbConfig.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.Ui;

/// <summary>
/// Endpoint filter that enforces <see cref="DbConfigUiOptions.Authorization"/> on every
/// request that enters the UI route group. When the request is unauthorized, the filter
/// short-circuits with an HTTP 401 (API callers), a 302 to
/// <see cref="DbConfigUiOptions.UnauthorizedRedirectUrl"/> (browser, custom redirect), or
/// lets the SPA shell render so the React app can show its own login page (browser,
/// built-in login enabled).
/// </summary>
internal sealed class DbConfigUiAuthFilter : IEndpointFilter
{
    private readonly DbConfigUiOptions _options;
    private readonly string _authPathPrefix;

    internal DbConfigUiAuthFilter(DbConfigUiOptions options, string prefix)
    {
        _options = options;
        _authPathPrefix = $"{prefix}/api/auth/";
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // /api/auth/* endpoints are always reachable: status is the way the SPA detects
        // login state in the first place, and login/logout are the credential exchange.
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (IsAuthApiPath(path))
        {
            return await next(context);
        }

        if (_options.Authorization is null)
        {
            return await next(context);
        }

        var authorized = await _options.Authorization.IsAuthorizedAsync(httpContext);
        if (authorized)
        {
            return await next(context);
        }

        var isBrowser = LooksLikeBrowserRequest(httpContext.Request);

        // Built-in login is enabled and the request looks like a browser navigation:
        // let the SPA shell render so the React app can show its own login page. The
        // SPA hits /api/auth/status (always reachable) on mount to decide what to render.
        if (isBrowser && _options.CredentialValidatorType is not null)
        {
            return await next(context);
        }

        if (isBrowser && !string.IsNullOrEmpty(_options.UnauthorizedRedirectUrl))
        {
            var returnUrl = Uri.EscapeDataString(httpContext.Request.Path + httpContext.Request.QueryString);
            var separator = _options.UnauthorizedRedirectUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';

            return Results.Redirect($"{_options.UnauthorizedRedirectUrl}{separator}returnUrl={returnUrl}");
        }

        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    private bool IsAuthApiPath(string path)
    {
        return path.StartsWith(_authPathPrefix, StringComparison.Ordinal);
    }

    private static bool LooksLikeBrowserRequest(HttpRequest request)
    {
        var accept = request.Headers.Accept.ToString();

        return accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            || accept.Length == 0;
    }
}

/// <summary>
/// Cookie-based <see cref="IDbConfigAuthorizationFilter"/> wired automatically when
/// <see cref="DbConfigUiOptions.UseBuiltInLogin{TValidator}"/> has been called.
/// Validates the signed auth cookie via <see cref="IDataProtector"/>.
/// </summary>
internal sealed class CookieAuthorizationFilter : IDbConfigAuthorizationFilter
{
    private readonly IDataProtector _protector;
    private readonly string _cookieName;

    internal CookieAuthorizationFilter(IDataProtector protector, string cookieName)
    {
        _protector = protector;
        _cookieName = cookieName;
    }

    public Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        var cookie = context.Request.Cookies[_cookieName];
        if (string.IsNullOrEmpty(cookie))
        {
            return Task.FromResult(false);
        }

        try
        {
            var payload = _protector.Unprotect(cookie);

            return Task.FromResult(payload.StartsWith("dbconfig|", StringComparison.Ordinal));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Task.FromResult(false);
        }
    }
}
