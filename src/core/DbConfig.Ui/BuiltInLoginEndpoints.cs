using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DbConfig.Ui;

/// <summary>
/// Registers <c>/login</c> (GET + POST) and <c>/logout</c> (POST) endpoints when
/// <see cref="DbConfigUiOptions.UseBuiltInLogin{TValidator}"/> is configured.
/// The login form is generated inline (no embedded resource); the cookie value is
/// signed with ASP.NET Data Protection.
/// </summary>
internal static class BuiltInLoginEndpoints
{
    internal const string ProtectorPurpose = "Moberg.DbConfig.Ui.Auth";
    internal const string CookiePayloadPrefix = "dbconfig|";

    internal static async Task HandleLoginGetAsync(HttpContext context, DbConfigUiOptions options, string prefix)
    {
        var returnUrl = context.Request.Query["returnUrl"].FirstOrDefault();
        var safeReturn = SanitizeReturnUrl(returnUrl, prefix);
        var error = string.Equals(context.Request.Query["error"].FirstOrDefault(), "1", StringComparison.Ordinal);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/html;charset=utf-8";
        await context.Response.WriteAsync(
            BuildLoginPage(prefix, safeReturn, error),
            Encoding.UTF8,
            context.RequestAborted);
    }

    internal static async Task HandleLoginPostAsync(HttpContext context, DbConfigUiOptions options, string prefix)
    {
        if (options.CredentialValidatorType is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var username = form["username"].FirstOrDefault() ?? string.Empty;
        var password = form["password"].FirstOrDefault() ?? string.Empty;
        var returnUrl = SanitizeReturnUrl(form["returnUrl"].FirstOrDefault(), prefix);

        var validator = context.RequestServices.GetService(typeof(IDbConfigCredentialValidator)) as IDbConfigCredentialValidator
            ?? throw new InvalidOperationException(
                "IDbConfigCredentialValidator is not registered. Call services.AddScoped<IDbConfigCredentialValidator, MyValidator>() before MapDbConfigUi.");

        var principal = await validator.ValidateAsync(username, password, context.RequestAborted);
        if (principal is null || principal.Identity is null || !principal.Identity.IsAuthenticated)
        {
            context.Response.Redirect($"{prefix}/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}");

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
            Path = prefix,
            Expires = DateTimeOffset.UtcNow.Add(options.CookieExpireTimeSpan),
        });

        context.Response.Redirect(returnUrl);
    }

    internal static Task HandleLogoutPostAsync(HttpContext context, DbConfigUiOptions options, string prefix)
    {
        context.Response.Cookies.Delete(options.CookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = prefix,
        });
        context.Response.Redirect($"{prefix}/login");

        return Task.CompletedTask;
    }

    private static string SanitizeReturnUrl(string? returnUrl, string prefix)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/'))
        {
            return prefix;
        }

        // Reject protocol-relative URLs (//evil.example) and CRLF injection attempts.
        if (returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.Contains('\r', StringComparison.Ordinal)
            || returnUrl.Contains('\n', StringComparison.Ordinal))
        {
            return prefix;
        }

        return returnUrl;
    }

    private static string BuildLoginPage(string prefix, string returnUrl, bool error)
    {
        var encoder = HtmlEncoder.Default;
        var errorBanner = error
            ? "<p class='err'>Invalid credentials. Try again.</p>"
            : string.Empty;

        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset='utf-8'>
            <title>db-config sign in</title>
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <style>
              body{font-family:system-ui,-apple-system,Segoe UI,sans-serif;max-width:380px;margin:80px auto;padding:0 24px;color:#111;background:#fff}
              h1{font-size:1.25rem;margin:0 0 6px}
              p{color:#555;margin:0 0 18px}
              p.err{color:#b00020}
              label{display:block;font-size:.85rem;color:#333;margin-top:14px}
              input,button{font:inherit;padding:10px 12px;width:100%;box-sizing:border-box;margin-top:6px;border:1px solid #ccc;border-radius:6px}
              button{background:#0070f3;color:#fff;border:0;cursor:pointer;font-weight:600;margin-top:18px}
              button:hover{background:#005bce}
              @media (prefers-color-scheme: dark) {
                body{background:#111;color:#eee}
                p{color:#aaa}
                input{background:#1d1d1d;color:#eee;border-color:#444}
                label{color:#bbb}
              }
            </style></head><body>
            <h1>Sign in</h1>
            <p>db-config admin</p>
            {{errorBanner}}
            <form method='post' action='{{encoder.Encode(prefix)}}/login'>
              <input type='hidden' name='returnUrl' value='{{encoder.Encode(returnUrl)}}' />
              <label>Username<input type='text' name='username' autocomplete='username' autofocus required /></label>
              <label>Password<input type='password' name='password' autocomplete='current-password' required /></label>
              <button type='submit'>Sign in</button>
            </form>
            </body></html>
            """;
    }
}
