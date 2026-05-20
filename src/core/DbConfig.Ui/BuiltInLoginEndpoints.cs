using System.Text;
using System.Text.Json;
using DbConfig.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.Ui;

/// <summary>
/// Registers JSON auth endpoints under <c>/api/auth/*</c> when
/// <see cref="DbConfigUiOptions.UseBuiltInLogin{TValidator}"/> is configured. The
/// login UI itself is rendered by the React SPA — the catch-all serves
/// <c>index.html</c> for <c>/login</c>; this class only exposes the JSON contract
/// the SPA calls (status, login, logout). Mirrors sister project Warp.
/// </summary>
internal static class BuiltInLoginEndpoints
{
    internal const string ProtectorPurpose = "Moberg.DbConfig.Ui.Auth";
    internal const string CookiePayloadPrefix = "dbconfig|";

    internal static async Task HandleAuthStatusAsync(HttpContext context, DbConfigUiOptions options)
    {
        var hasBuiltInLogin = options.CredentialValidatorType is not null;
        var authenticated = false;
        string? username = null;

        if (options.Authorization is not null)
        {
            authenticated = await options.Authorization.IsAuthorizedAsync(context);
        }
        else if (!hasBuiltInLogin)
        {
            // No filter, no built-in login — UI is open. Report authenticated so the
            // SPA does not gate the dashboard behind a login flow.
            authenticated = true;
        }

        if (authenticated && hasBuiltInLogin)
        {
            username = TryExtractUsernameFromCookie(context, options);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            authenticated,
            hasBuiltInLogin,
            username,
        };
        var json = JsonSerializer.Serialize(payload);

        await context.Response.WriteAsync(json, Encoding.UTF8, context.RequestAborted);
    }

    internal static async Task HandleLoginPostAsync(HttpContext context, DbConfigUiOptions options, string prefix)
    {
        if (options.CredentialValidatorType is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        LoginRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<LoginRequest>(
                context.Request.Body,
                JsonOptions,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status400BadRequest, "Invalid JSON body");

            return;
        }

        var username = body?.Username ?? string.Empty;
        var password = body?.Password ?? string.Empty;

        var validator = context.RequestServices.GetService(typeof(IDbConfigCredentialValidator)) as IDbConfigCredentialValidator
            ?? throw new InvalidOperationException(
                "IDbConfigCredentialValidator is not registered. Call services.AddScoped<IDbConfigCredentialValidator, MyValidator>() before MapDbConfigUi.");

        var principal = await validator.ValidateAsync(username, password, context.RequestAborted);
        if (principal is null || principal.Identity is null || !principal.Identity.IsAuthenticated)
        {
            await WriteJsonErrorAsync(context, StatusCodes.Status401Unauthorized, "Invalid credentials");

            return;
        }

        var protector = context.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ProtectorPurpose);

        var identityName = principal.Identity.Name ?? username;
        var token = protector.Protect($"{CookiePayloadPrefix}{identityName}|{DateTimeOffset.UtcNow:O}");

        context.Response.Cookies.Append(options.CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = options.CookiePath ?? prefix,
            Expires = DateTimeOffset.UtcNow.Add(options.CookieExpireTimeSpan),
        });

        await WriteJsonOkAsync(context);
    }

    internal static async Task HandleLogoutPostAsync(HttpContext context, DbConfigUiOptions options, string prefix)
    {
        context.Response.Cookies.Delete(options.CookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = options.CookiePath ?? prefix,
        });

        await WriteJsonOkAsync(context);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task WriteJsonOkAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync("{\"ok\":true}", Encoding.UTF8, context.RequestAborted);
    }

    private static async Task WriteJsonErrorAsync(HttpContext context, int statusCode, string error)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = JsonSerializer.Serialize(new { error });
        await context.Response.WriteAsync(payload, Encoding.UTF8, context.RequestAborted);
    }

    private static string? TryExtractUsernameFromCookie(HttpContext context, DbConfigUiOptions options)
    {
        var cookie = context.Request.Cookies[options.CookieName];
        if (string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        try
        {
            var protector = context.RequestServices
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(ProtectorPurpose);
            var payload = protector.Unprotect(cookie);
            if (!payload.StartsWith(CookiePayloadPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            // Payload format: "dbconfig|<username>|<iso-timestamp>"
            var afterPrefix = payload[CookiePayloadPrefix.Length..];
            var separator = afterPrefix.IndexOf('|', StringComparison.Ordinal);

            return separator < 0 ? afterPrefix : afterPrefix[..separator];
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private sealed record LoginRequest(string? Username, string? Password);
}
