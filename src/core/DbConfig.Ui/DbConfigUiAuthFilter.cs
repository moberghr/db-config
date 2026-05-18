using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.Ui;

/// <summary>
/// Endpoint filter that enforces <see cref="DbConfigUiOptions.Authorization"/> on every
/// request that enters the UI route group. When the request is unauthorized, the filter
/// short-circuits with an HTTP 401 (API/non-browser callers) or a 302 to either the
/// built-in <c>/login</c> page or <see cref="DbConfigUiOptions.UnauthorizedRedirectUrl"/>.
/// </summary>
internal sealed class DbConfigUiAuthFilter : IEndpointFilter
{
    private readonly DbConfigUiOptions _options;
    private readonly string _prefix;
    private readonly string _loginPath;

    internal DbConfigUiAuthFilter(DbConfigUiOptions options, string prefix)
    {
        _options = options;
        _prefix = prefix;
        _loginPath = $"{prefix}/login";
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // /login and /logout endpoints are always reachable; the login endpoints
        // themselves are the way to acquire credentials in the first place.
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (IsLoginOrLogoutPath(path))
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

        return UnauthorizedResult(httpContext);
    }

    private bool IsLoginOrLogoutPath(string path)
    {
        return string.Equals(path, _loginPath, StringComparison.Ordinal)
            || string.Equals(path, $"{_prefix}/logout", StringComparison.Ordinal);
    }

    private IResult UnauthorizedResult(HttpContext httpContext)
    {
        var isBrowser = LooksLikeBrowserRequest(httpContext.Request);

        if (isBrowser && _options.CredentialValidatorType is not null)
        {
            var returnUrl = Uri.EscapeDataString(httpContext.Request.Path + httpContext.Request.QueryString);

            return Results.Redirect($"{_loginPath}?returnUrl={returnUrl}");
        }

        if (isBrowser && !string.IsNullOrEmpty(_options.UnauthorizedRedirectUrl))
        {
            var returnUrl = Uri.EscapeDataString(httpContext.Request.Path + httpContext.Request.QueryString);
            var separator = _options.UnauthorizedRedirectUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';

            return Results.Redirect($"{_options.UnauthorizedRedirectUrl}{separator}returnUrl={returnUrl}");
        }

        return Results.StatusCode(StatusCodes.Status401Unauthorized);
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
