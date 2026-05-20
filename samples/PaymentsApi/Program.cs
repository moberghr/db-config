// Multi-tenant payments processor sample for db-config.
// NOT FOR PRODUCTION: built-in cookie login (one shared password) for the unified
// admin surface, in-memory mock for Stripe.

using DbConfig.Core;
using DbConfig.Http;
using DbConfig.Provider.PostgreSql;
using DbConfig.Ui;
using Microsoft.Extensions.Options;
using PaymentsApi;
using PaymentsApi.Options;

var builder = WebApplication.CreateBuilder(args);

// --- DbConfig wireup (single-call) ---
var connectionString = builder.Configuration.GetConnectionString("PaymentsApi")
    ?? throw new InvalidOperationException("ConnectionStrings:PaymentsApi is required.");

var dbConfigScope = builder.Configuration["DbConfig:Scope"] ?? "PaymentsApi";
var reloadSeconds = int.TryParse(builder.Configuration["DbConfig:ReloadIntervalSeconds"], out var r) ? r : 5;

builder.Services.AddHttpContextAccessor();

// Schema is auto-applied during AddDbConfig (SchemaMode.CreateIfMissing, the default).
// Production hosts that prefer DBA-controlled or CI-pipeline schema management can opt out
// via b.Options.SchemaMode = SchemaMode.None and apply migrations out of band with
// DbConfigMigrator.MigrateAsync(...) or GenerateMigrationScript(...).
builder.AddDbConfig(b =>
{
    b.Options.Scope = dbConfigScope;
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
using (var diScope = app.Services.CreateScope())
{
    var store = diScope.ServiceProvider.GetRequiredService<IConfigStore>();
    var logger = diScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await SeedDemoDataAsync(store, app.Environment.EnvironmentName, dbConfigScope, logger);
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
    + "immediately after sign-in — no Scope/Environment input required). Browser flow "
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

// GET /api/diag/reader-stripe/{tenantId} — v0.11.2 ITenantConfigReader demo.
//
// ITenantConfigReader.GetForTenant<T>(tenantId) binds T using the SAME section path the
// consumer registered via services.Configure<T>(GetSection("Stripe")) — no parallel namespace,
// no typeof(T).Name convention. Internally it sets an AsyncLocal tenant override on the
// polling provider and resolves IOptionsSnapshot<T> in a fresh DI scope, so PostConfigure
// delegates and other configurators run exactly as for a normal request.
app.MapGet("/api/diag/reader-stripe/{tenantId}", (
    string tenantId, ITenantConfigReader reader) =>
{
    var stripe = reader.GetForTenant<StripeOptions>(tenantId);

    return Results.Ok(new
    {
        tenantId,
        apiKeyPrefix = SafePrefix(stripe.ApiKey, 12),
        defaultCurrency = stripe.DefaultCurrency,
        webhookSecretPrefix = SafePrefix(stripe.WebhookSecret, 8),
        note = "Read via ITenantConfigReader.GetForTenant<StripeOptions>(tenantId). "
            + "Uses the same 'Stripe' section as IOptionsSnapshot<StripeOptions> — "
            + "AsyncLocal override pins the tenant for the bind.",
    });
});

// GET /api/diag/cross-tenant-stripe/{tenantId} — v0.11.1 convenience API demo.
//
// IConfigStore.GetForTenantAsync<T>(tenantId) reads the full StripeOptions POCO for an
// EXPLICIT tenant id (not necessarily the request's). The section name is typeof(T).Name
// verbatim → "StripeOptions:" — so this endpoint reads from a parallel set of seed entries
// (StripeOptions:*) intentionally added below alongside the existing "Stripe:*" entries
// used by the per-request IOptionsSnapshot pipeline. No prefix-stripping, no convention
// magic — the type name IS the section name.
app.MapGet("/api/diag/cross-tenant-stripe/{tenantId}", async (
    string tenantId, IConfigStore store, CancellationToken ct) =>
{
    var stripe = await store.GetForTenantAsync<StripeOptions>(tenantId, ct);

    return Results.Ok(new
    {
        tenantId,
        apiKeyPrefix = SafePrefix(stripe.ApiKey, 12),
        defaultCurrency = stripe.DefaultCurrency,
        webhookSecretPrefix = SafePrefix(stripe.WebhookSecret, 8),
        note = "Read via IConfigStore.GetForTenantAsync<StripeOptions>(tenantId). "
            + "Section name is typeof(T).Name verbatim → 'StripeOptions:'.",
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

static async Task SeedDemoDataAsync(IConfigStore store, string env, string scope, ILogger logger)
{
    var existing = await store.GetAllForAllTenantsAsync(scope, env, CancellationToken.None);
    if (existing.Count > 0)
    {
        logger.LogInformation("Skipping demo seed — {Count} entries already present", existing.Count);

        return;
    }

    var now = DateTimeOffset.UtcNow;
    var entries = new List<ConfigEntry>
    {
        // Global config (TenantId = "")
        // Depth-0 secret — top-level encryption key.
        new(scope, env, "", "MasterEncryptionKey", "DEMO_master_key_NOT_REAL", true, now, "seed"),
        // Depth-0 plaintext — top-level non-secret.
        new(scope, env, "", "DefaultLocale", "en-US", false, now, "seed"),

        // Depth-1 entries (Stripe:*, Limits:*, Features:*) — both secret and plaintext.
        new(scope, env, "", "Stripe:WebhookSecret", "whsec_DEMO_global_webhook", true, now, "seed"),
        new(scope, env, "", "Stripe:DefaultCurrency", "USD", false, now, "seed"),
        new(scope, env, "", "Stripe:IdempotencyWindowSeconds", "60", false, now, "seed"),
        new(scope, env, "", "Limits:DailyChargeCap", "1000000", false, now, "seed"),
        new(scope, env, "", "Limits:MaxChargeAmount", "50000", false, now, "seed"),
        new(scope, env, "", "Features:BetaSplitPayments", "false", false, now, "seed"),

        // Depth-2 secrets — 3-segment keys with IsSecret=true.
        new(scope, env, "", "Stripe:OAuth:ClientSecret", "ca_DEMO_oauth_client_secret", true, now, "seed"),
        new(scope, env, "", "Stripe:OAuth:ClientId", "ca_demo_client_id", false, now, "seed"),
        new(scope, env, "", "Database:Primary:Password", "DEMO_db_password_NOT_REAL", true, now, "seed"),
        new(scope, env, "", "Database:Primary:Host", "db.internal.example", false, now, "seed"),
        new(scope, env, "", "Database:Primary:Port", "5432", false, now, "seed"),
        new(scope, env, "", "Database:Replica:Password", "DEMO_replica_password_NOT_REAL", true, now, "seed"),
        new(scope, env, "", "Database:Replica:Host", "db-replica.internal.example", false, now, "seed"),

        // 3-level nested: Notifications:Email:* — gives the tree view real hierarchy
        new(scope, env, "", "Notifications:Email:Templates:Welcome", "Hi {name}, welcome to PaymentsApi.", false, now, "seed"),
        new(scope, env, "", "Notifications:Email:Templates:PaymentFailed", "Your payment of {amount} failed.", false, now, "seed"),
        new(scope, env, "", "Notifications:Email:Smtp:Host", "smtp.sendgrid.net", false, now, "seed"),
        new(scope, env, "", "Notifications:Email:Smtp:Port", "587", false, now, "seed"),
        new(scope, env, "", "Notifications:Email:Smtp:UseTls", "true", false, now, "seed"),
        new(scope, env, "", "Notifications:Email:Smtp:Username", "apikey", false, now, "seed"),
        new(scope, env, "", "Notifications:Email:Smtp:Password", "SG.DEMO-PASSWORD-NOT-REAL", true, now, "seed"),

        // 4-level nested experiment tree
        new(scope, env, "", "Features:Experiments:Checkout:V2:Enabled", "false", false, now, "seed"),
        new(scope, env, "", "Features:Experiments:Checkout:V2:RolloutPct", "0", false, now, "seed"),

        // v0.11.1 parallel "StripeOptions:" namespace — the convenience API
        // GetForTenantAsync<StripeOptions>(...) binds from verbatim type name. We seed both
        // namespaces so the existing per-request IOptionsSnapshot path (Stripe:) keeps working
        // alongside the new typed-binder demo (StripeOptions:).
        new(scope, env, "", "StripeOptions:WebhookSecret", "whsec_DEMO_global_webhook", true, now, "seed"),
        new(scope, env, "", "StripeOptions:DefaultCurrency", "USD", false, now, "seed"),
        new(scope, env, "", "StripeOptions:IdempotencyWindowSeconds", "60", false, now, "seed"),
        new(scope, env, "Acme", "StripeOptions:ApiKey", "sk_test_DEMO_acme_key", true, now, "seed"),
        new(scope, env, "Acme", "StripeOptions:DefaultCurrency", "EUR", false, now, "seed"),
        new(scope, env, "Globex", "StripeOptions:ApiKey", "sk_test_DEMO_globex_key", true, now, "seed"),

        // Tenant Acme overrides
        new(scope, env, "Acme", "Stripe:ApiKey", "sk_test_DEMO_acme_key", true, now, "seed"),
        new(scope, env, "Acme", "Stripe:DefaultCurrency", "EUR", false, now, "seed"),
        new(scope, env, "Acme", "Features:NewCheckout", "true", false, now, "seed"),
        new(scope, env, "Acme", "Notifications:SlackWebhook", "https://hooks.slack.com/acme/DEMO", true, now, "seed"),
        new(scope, env, "Acme", "Notifications:OnFailureEmail", "ops@acme.example", false, now, "seed"),
        new(scope, env, "Acme", "Notifications:Email:Templates:Welcome", "Welcome to Acme via PaymentsApi.", false, now, "seed"),

        // Tenant Globex overrides
        new(scope, env, "Globex", "Stripe:ApiKey", "sk_test_DEMO_globex_key", true, now, "seed"),
        new(scope, env, "Globex", "Limits:MaxChargeAmount", "100000", false, now, "seed"),
        new(scope, env, "Globex", "Features:Require3DS", "true", false, now, "seed"),

        // A second app's entries — exercises the flat /entries endpoint's
        // cross-Scope view in the admin UI.
        new("Notifications", env, "", "Email:Smtp:Host", "smtp.gmail.com", false, now, "seed"),
        new("Notifications", env, "", "Slack:DefaultChannel", "#alerts", false, now, "seed"),
        new("Notifications", env, "Acme", "Slack:DefaultChannel", "#acme-alerts", false, now, "seed"),
    };

    foreach (var entry in entries)
    {
        await store.UpsertAsync(entry, CancellationToken.None);
    }

    // Additional operations to give the global Audit Log page interesting variety.
    //
    // 1. Stripe:DefaultCurrency: USD (seeded above) → EUR → GBP. Produces two Update
    //    audit rows on top of the initial Insert.
    // 2. Legacy:OldSetting: Insert then Delete. The entry no longer exists, but its
    //    audit trail (Insert + Delete) remains reachable only via the new global
    //    Audit Log page.
    await store.UpsertAsync(
        new ConfigEntry(scope, env, "", "Stripe:DefaultCurrency", "EUR", false, now.AddMinutes(5), "platform-admin"),
        CancellationToken.None);
    await store.UpsertAsync(
        new ConfigEntry(scope, env, "", "Stripe:DefaultCurrency", "GBP", false, now.AddMinutes(10), "platform-admin"),
        CancellationToken.None);

    await store.UpsertAsync(
        new ConfigEntry(scope, env, "", "Legacy:OldSetting", "deprecated", false, now.AddMinutes(15), "platform-admin"),
        CancellationToken.None);
    await store.DeleteAsync(scope, env, "Legacy:OldSetting", CancellationToken.None);

    logger.LogInformation("Seeded {Count} demo config entries for {App}/{Env}", entries.Count, scope, env);
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
