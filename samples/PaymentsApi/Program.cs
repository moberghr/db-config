// Multi-tenant payments processor sample for db-config.
// NOT FOR PRODUCTION: cookie login + static API-key header for admin auth, in-memory mock for Stripe.

using System.Security.Claims;
using System.Text.Encodings.Web;
using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Http;
using DbConfig.Provider.PostgreSql;
using DbConfig.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentsApi;
using PaymentsApi.Options;

var builder = WebApplication.CreateBuilder(args);

// --- DbConfig wireup (single-call) ---
var connectionString = builder.Configuration.GetConnectionString("PaymentsApi")
    ?? throw new InvalidOperationException("ConnectionStrings:PaymentsApi is required.");

var appName = builder.Configuration["DbConfig:AppName"] ?? "PaymentsApi";
var reloadSeconds = int.TryParse(builder.Configuration["DbConfig:ReloadIntervalSeconds"], out var r) ? r : 5;

builder.Services.AddHttpContextAccessor();

builder.AddDbConfig(b =>
{
    b.Options.AppName = appName;
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(reloadSeconds);
    b.UsePostgreSql(connectionString);
    b.AddTenantResolver<HeaderTenantResolver>();
});

// Typed options — IOptionsSnapshot<T> is tenant-aware automatically.
builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<FeatureFlagsOptions>(builder.Configuration.GetSection("Features"));
builder.Services.Configure<PaymentLimitsOptions>(builder.Configuration.GetSection("Limits"));
builder.Services.Configure<NotificationsOptions>(builder.Configuration.GetSection("Notifications"));

// --- Auth: cookie login (browser) + static API-key header (curl) — NOT FOR PROD ---
// Cookie scheme is default so browser navigation to /admin/dbconfig auto-redirects to /login.
// API-key scheme accepts X-Admin-Api-Key for curl/Postman. Both satisfy AdminPolicy.
const string AdminPolicy = "Admin";
const string ApiKeyScheme = "ApiKey";

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "payments-demo-auth";
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyScheme, null);

builder.Services.AddAuthorization(o =>
    o.AddPolicy(AdminPolicy, p =>
    {
        p.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, ApiKeyScheme);
        p.RequireAuthenticatedUser();
    }));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Apply EF migrations on startup (demo only).
var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
    .UseNpgsql(
        connectionString,
        npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
    .Options;

await using (var ctx = new DbConfigDbContext(migrateOptions))
{
    await ctx.Database.MigrateAsync();
}

// Idempotent seed — runs once if the store is empty.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<IConfigStore>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await SeedDemoDataAsync(store, app.Environment.EnvironmentName, appName, logger);
}

// --- DbConfig admin surface — gated by ApiKey policy ---
app.MapDbConfigHttp("/api/dbconfig").RequireAuthorization(AdminPolicy);
app.MapDbConfigUi("/admin/dbconfig", "/api/dbconfig").RequireAuthorization(AdminPolicy);

// --- Landing ---
app.MapGet("/", () =>
    "PaymentsApi sample for db-config. Admin UI at /admin/dbconfig (browser: sign in at /login; curl: X-Admin-Api-Key). Try /api/diag/who.");

// --- Demo login (cookie-based; for browser-driven UI walkthroughs) ---
app.MapGet("/login", (string? error, string? ReturnUrl) =>
{
    var safeReturn = string.IsNullOrEmpty(ReturnUrl) || !ReturnUrl.StartsWith('/')
        ? "/admin/dbconfig"
        : ReturnUrl;
    var errorBanner = error == "1"
        ? "<p style='color:#b00020;margin:0 0 12px'>Invalid key. Try again.</p>"
        : string.Empty;
    var html = $$"""
        <!doctype html>
        <html><head><meta charset='utf-8'><title>db-config demo login</title>
        <style>
          body{font-family:system-ui;max-width:420px;margin:80px auto;padding:0 24px;color:#111}
          h1{font-size:1.4rem;margin:0 0 6px}
          p{color:#555;margin:0 0 18px}
          input,button{font:inherit;padding:10px 12px;width:100%;box-sizing:border-box;margin-top:10px;border:1px solid #ccc;border-radius:6px}
          button{background:#0070f3;color:#fff;border:0;cursor:pointer;font-weight:600}
          code{background:#f4f4f4;padding:1px 6px;border-radius:4px}
        </style></head><body>
        <h1>db-config demo</h1>
        <p>Sign in with the value of <code>Auth:ApiKey</code> from <code>appsettings.json</code> (default: <code>demo-admin-key-12345</code>).</p>
        {{errorBanner}}
        <form method='post' action='/login'>
          <input type='hidden' name='returnUrl' value='{{HtmlEncoder.Default.Encode(safeReturn)}}' />
          <input type='password' name='apiKey' placeholder='Admin API key' autofocus required />
          <button type='submit'>Sign in</button>
        </form>
        </body></html>
        """;

    return Results.Content(html, "text/html");
}).AllowAnonymous();

app.MapPost("/login", async (HttpContext ctx, IConfiguration cfg) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var apiKey = form["apiKey"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    var expected = cfg["Auth:ApiKey"];

    if (string.IsNullOrEmpty(expected) || !string.Equals(apiKey, expected, StringComparison.Ordinal))
    {
        return Results.Redirect("/login?error=1");
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "demo-admin")],
        CookieAuthenticationDefaults.AuthenticationScheme);

    await ctx.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/'))
    {
        returnUrl = "/admin/dbconfig";
    }

    return Results.Redirect(returnUrl);
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/login");
}).AllowAnonymous();

// =====================================================================
// Business endpoints — NO AUTH on these in the demo. Wire your own.
// =====================================================================

// POST /api/charges — uses per-tenant Stripe key, currency, limits, flags.
app.MapPost("/api/charges", (
    ChargeRequest req,
    ITenantResolver tenants,
    IOptionsSnapshot<StripeOptions> stripeOpts,
    IOptionsSnapshot<PaymentLimitsOptions> limitsOpts,
    IOptionsSnapshot<FeatureFlagsOptions> featureOpts) =>
{
    var tenantId = tenants.Resolve() ?? string.Empty;
    var stripe = stripeOpts.Value;
    var limits = limitsOpts.Value;
    var flags = featureOpts.Value;

    if (req.Amount > limits.MaxChargeAmount)
    {
        return Results.UnprocessableEntity(new
        {
            error = "amount_exceeds_max_charge",
            requested = req.Amount,
            max = limits.MaxChargeAmount,
            tenantId,
        });
    }

    var currency = string.IsNullOrEmpty(req.Currency) ? stripe.DefaultCurrency : req.Currency;
    var apiKeyPrefix = SafePrefix(stripe.ApiKey, 12);

    return Results.Ok(new
    {
        chargeId = Guid.NewGuid().ToString("N"),
        tenantId = string.IsNullOrEmpty(tenantId) ? "(none — global defaults)" : tenantId,
        stripeApiKeyPrefix = apiKeyPrefix,
        currency,
        amount = req.Amount,
        customerId = req.CustomerId,
        appliedFlags = new
        {
            flags.NewCheckout,
            flags.Require3DS,
            flags.BetaSplitPayments,
        },
    });
});

// POST /api/refunds — uses per-tenant Stripe key.
app.MapPost("/api/refunds", (
    RefundRequest req,
    ITenantResolver tenants,
    IOptionsSnapshot<StripeOptions> stripeOpts) =>
{
    var tenantId = tenants.Resolve() ?? string.Empty;
    var stripe = stripeOpts.Value;
    var apiKeyPrefix = SafePrefix(stripe.ApiKey, 12);

    return Results.Ok(new
    {
        refundId = Guid.NewGuid().ToString("N"),
        chargeId = req.ChargeId,
        amount = req.Amount,
        tenantId = string.IsNullOrEmpty(tenantId) ? "(none — global defaults)" : tenantId,
        stripeApiKeyPrefix = apiKeyPrefix,
    });
});

// POST /webhooks/stripe — verifies signature against the GLOBAL webhook secret.
// Real implementation would HMAC-verify the payload with Stripe-Signature.
app.MapPost("/webhooks/stripe", (
    HttpRequest httpReq,
    IOptionsSnapshot<StripeOptions> stripeOpts) =>
{
    var signature = httpReq.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;
    var expected = stripeOpts.Value.WebhookSecret;

    // Mock verification: any non-empty signature passes when a webhook secret is configured.
    // Real impl: HMAC-SHA256(payload, expected) and compare in constant time.
    var verified = !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(signature);

    return Results.Ok(new
    {
        verified,
        webhookSecretConfigured = !string.IsNullOrEmpty(expected),
        note = "Mock signature check. Production hosts must HMAC-verify against the raw body.",
    });
});

// GET /api/diag/config — full resolved config for the current tenant (secrets masked).
app.MapGet("/api/diag/config", (
    ITenantResolver tenants,
    IOptionsSnapshot<StripeOptions> stripeOpts,
    IOptionsSnapshot<FeatureFlagsOptions> featureOpts,
    IOptionsSnapshot<PaymentLimitsOptions> limitsOpts,
    IOptionsSnapshot<NotificationsOptions> notificationsOpts) =>
{
    var tenantId = tenants.Resolve() ?? string.Empty;
    var stripe = stripeOpts.Value;
    var notifications = notificationsOpts.Value;

    return Results.Ok(new
    {
        tenantId = string.IsNullOrEmpty(tenantId) ? "(none — global defaults)" : tenantId,
        stripe = new
        {
            apiKey = MaskSecret(stripe.ApiKey),
            webhookSecret = MaskSecret(stripe.WebhookSecret),
            stripe.DefaultCurrency,
            stripe.IdempotencyWindowSeconds,
        },
        features = featureOpts.Value,
        limits = limitsOpts.Value,
        notifications = new
        {
            slackWebhook = MaskSecret(notifications.SlackWebhook),
            notifications.OnFailureEmail,
        },
    });
});

// GET /api/diag/feature-flags — lightweight subset for live-reload demos.
app.MapGet("/api/diag/feature-flags", (
    ITenantResolver tenants,
    IOptionsSnapshot<FeatureFlagsOptions> featureOpts) =>
{
    var tenantId = tenants.Resolve() ?? string.Empty;

    return Results.Ok(new
    {
        tenantId = string.IsNullOrEmpty(tenantId) ? "(none — global defaults)" : tenantId,
        flags = featureOpts.Value,
    });
});

// GET /api/diag/io — side-by-side IOptions<T> vs IOptionsSnapshot<T> to expose
// the IOptions singleton-cache gotcha. IOptions binds ONCE at first access (no
// request scope, resolver returns null), so it permanently reflects global config.
app.MapGet("/api/diag/io", (
    ITenantResolver tenants,
    IOptions<StripeOptions> ioptions,
    IOptionsSnapshot<StripeOptions> snapshot) =>
{
    var tenantId = tenants.Resolve() ?? string.Empty;

    return Results.Ok(new
    {
        tenantId = string.IsNullOrEmpty(tenantId) ? "(none — global defaults)" : tenantId,
        note = "IOptions<T> is singleton-cached and bound once at startup without a request "
            + "scope (the resolver returns null). It will NEVER reflect per-tenant values. "
            + "IOptionsSnapshot<T> is scoped per-request and rebinds with the current tenant.",
        ioptions_value = new
        {
            apiKeyPrefix = SafePrefix(ioptions.Value.ApiKey, 12),
            ioptions.Value.DefaultCurrency,
        },
        ioptions_snapshot_value = new
        {
            apiKeyPrefix = SafePrefix(snapshot.Value.ApiKey, 12),
            snapshot.Value.DefaultCurrency,
        },
    });
});

// GET /api/diag/who — smoke test for the resolver.
app.MapGet("/api/diag/who", (ITenantResolver tenants) =>
{
    var tenantId = tenants.Resolve();

    return Results.Ok(new
    {
        resolvedTenantId = tenantId,
        hint = string.IsNullOrEmpty(tenantId)
            ? "No X-Tenant-Id header — IConfiguration will return global defaults."
            : $"Tenant '{tenantId}' resolved — IOptionsSnapshot<T> will bind tenant-specific values.",
    });
});

await app.RunAsync();

// ====================================================================
// Local helpers (top-level methods are emitted as static on Program).
// ====================================================================

static async Task SeedDemoDataAsync(IConfigStore store, string env, string appName, ILogger logger)
{
    var existing = await store.GetAllForAllTenantsAsync(appName, env, CancellationToken.None);
    if (existing.Count > 0)
    {
        logger.LogInformation("Skipping demo seed — {Count} entries already present", existing.Count);

        return;
    }

    var now = DateTimeOffset.UtcNow;
    var entries = new List<ConfigEntry>
    {
        // Global config (TenantId = "")
        new(appName, env, "", "Stripe:WebhookSecret", "whsec_DEMO_global_webhook", true, now, "seed"),
        new(appName, env, "", "Stripe:DefaultCurrency", "USD", false, now, "seed"),
        new(appName, env, "", "Stripe:IdempotencyWindowSeconds", "60", false, now, "seed"),
        new(appName, env, "", "Limits:DailyChargeCap", "1000000", false, now, "seed"),
        new(appName, env, "", "Limits:MaxChargeAmount", "50000", false, now, "seed"),
        new(appName, env, "", "Features:BetaSplitPayments", "false", false, now, "seed"),

        // Tenant Acme overrides
        new(appName, env, "Acme", "Stripe:ApiKey", "sk_test_DEMO_acme_key", true, now, "seed"),
        new(appName, env, "Acme", "Stripe:DefaultCurrency", "EUR", false, now, "seed"),
        new(appName, env, "Acme", "Features:NewCheckout", "true", false, now, "seed"),
        new(appName, env, "Acme", "Notifications:SlackWebhook", "https://hooks.slack.com/acme/DEMO", true, now, "seed"),
        new(appName, env, "Acme", "Notifications:OnFailureEmail", "ops@acme.example", false, now, "seed"),

        // Tenant Globex overrides
        new(appName, env, "Globex", "Stripe:ApiKey", "sk_test_DEMO_globex_key", true, now, "seed"),
        new(appName, env, "Globex", "Limits:MaxChargeAmount", "100000", false, now, "seed"),
        new(appName, env, "Globex", "Features:Require3DS", "true", false, now, "seed"),
    };

    foreach (var entry in entries)
    {
        await store.UpsertAsync(entry, CancellationToken.None);
    }

    logger.LogInformation("Seeded {Count} demo config entries for {App}/{Env}", entries.Count, appName, env);
}

static string MaskSecret(string? value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    var prefix = value.Length >= 8 ? value[..8] : value;

    return prefix + "***";
}

static string SafePrefix(string? value, int n)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    return value.Length >= n ? value[..n] : value;
}
