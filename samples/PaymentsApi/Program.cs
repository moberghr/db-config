// Multi-tenant payments processor sample for db-config.
// NOT FOR PRODUCTION: built-in cookie login (one shared password) for the unified
// admin surface, in-memory mock for Stripe.

using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Http;
using DbConfig.Provider.PostgreSql;
using DbConfig.Ui;
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

// Apply EF migrations BEFORE AddDbConfig — the polling provider's first Load() runs
// synchronously during AddDbConfig, and Load() queries DbConfig_Entries.
await ApplyMigrationsAsync(connectionString);

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

// --- Auth wiring (NOT FOR PROD) ---
// The unified admin surface (UI + HTTP API under one prefix) uses db-config's built-in
// cookie login. The validator checks the submitted password against Auth:Password from
// appsettings.json. One cookie covers both /admin/dbconfig (UI) and /admin/dbconfig/api
// (the React app's HTTP backend).
builder.Services.AddScoped<IDbConfigCredentialValidator, AppSettingsCredentialValidator>();

var app = builder.Build();

// Idempotent seed — runs once if the store is empty.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<IConfigStore>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await SeedDemoDataAsync(store, app.Environment.EnvironmentName, appName, logger);
}

// --- DbConfig admin surface (one call, unified) ---
// MapDbConfigAdmin mounts:
//   - UI at  /admin/dbconfig
//   - API at /admin/dbconfig/api
// Both share the same cookie (Path = /admin/dbconfig) so the React app can call its own
// backend right after sign-in with no separate auth dance. Form lives at
// /admin/dbconfig/login; sign in with any username + the value of Auth:Password.
app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<AppSettingsCredentialValidator>());

// --- Landing ---
// The admin UI now loads every entry across all apps + environments + tenants on first paint
// (via the flat /admin/dbconfig/api/ endpoint added in v0.10.0). Operators can narrow with the
// optional filter fields in the toolbar — useful for production hosts with many apps.
app.MapGet("/", () =>
    "PaymentsApi sample for db-config. Admin UI at /admin/dbconfig (loads all entries "
    + "immediately after sign-in — no AppName/Environment input required). Browser flow "
    + "uses the built-in cookie login; sign in with any username and the value of "
    + "Auth:Password from appsettings.json. HTTP API at /admin/dbconfig/api (same cookie). "
    + "Try /api/diag/who.");

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

static async Task ApplyMigrationsAsync(string connectionString)
{
    var opts = new DbContextOptionsBuilder<DbConfigDbContext>()
        .UseNpgsql(connectionString, npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"))
        .Options;

    await using var ctx = new DbConfigDbContext(opts);
    await ctx.Database.MigrateAsync();
}

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
