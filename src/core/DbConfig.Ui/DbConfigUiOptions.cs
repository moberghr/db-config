using DbConfig.Http;

namespace DbConfig.Ui;

/// <summary>
/// Options for the DbConfig embedded admin UI. Configure via the
/// <see cref="EndpointRouteBuilderExtensions.MapDbConfigUi(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, string, string, System.Action{DbConfigUiOptions}?)"/>
/// overload that accepts an <see cref="Action{T}"/>.
/// </summary>
public class DbConfigUiOptions
{
    /// <summary>
    /// Authorization filter for the UI route group. <c>null</c> = allow all
    /// (default). When <see cref="UseBuiltInLogin{TValidator}"/> is set, this
    /// is auto-configured to validate the signed auth cookie.
    /// </summary>
    public IDbConfigAuthorizationFilter? Authorization { get; set; }

    /// <summary>
    /// URL to redirect to when an unauthorized browser request is detected.
    /// If set, browser requests get 302 with <c>?returnUrl=</c>. Takes
    /// precedence over the built-in login page. Ignored when
    /// <see cref="UseBuiltInLogin{TValidator}"/> is also configured (built-in
    /// login wins).
    /// </summary>
    public string? UnauthorizedRedirectUrl { get; set; }

    /// <summary>
    /// Cookie name for the built-in login. Default <c>"dbconfig-auth"</c>.
    /// </summary>
    public string CookieName { get; set; } = "dbconfig-auth";

    /// <summary>
    /// Sliding cookie expiry for the built-in login. Default <c>7 days</c>.
    /// </summary>
    public TimeSpan CookieExpireTimeSpan { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Path scope for the auth cookie. <c>null</c> (default) = the UI prefix.
    /// Override when the UI and the HTTP API live under a common ancestor path
    /// (e.g. set to <c>"/admin/dbconfig"</c> when UI is at <c>/admin/dbconfig</c>
    /// and API is at <c>/admin/dbconfig/api</c>). <c>MapDbConfigAdmin</c> sets
    /// this automatically.
    /// </summary>
    public string? CookiePath { get; set; }

    /// <summary>
    /// Type of the <see cref="IDbConfigCredentialValidator"/> implementation
    /// for the built-in login page. Set via
    /// <see cref="UseBuiltInLogin{TValidator}"/>. <c>null</c> = no built-in
    /// login.
    /// </summary>
    internal Type? CredentialValidatorType { get; private set; }

    /// <summary>
    /// Enables the built-in login page with the specified credential validator.
    /// The validator MUST be registered separately in DI (typically as scoped)
    /// before <c>MapDbConfigUi</c> is called — for example,
    /// <c>builder.Services.AddScoped&lt;IDbConfigCredentialValidator, MyValidator&gt;()</c>.
    /// </summary>
    public void UseBuiltInLogin<TValidator>()
        where TValidator : class, IDbConfigCredentialValidator
    {
        CredentialValidatorType = typeof(TValidator);
    }
}
