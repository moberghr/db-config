using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests for <see cref="ITenantConfigReader"/>: binds <c>IOptionsSnapshot&lt;T&gt;</c> for an
/// explicit tenant id via an AsyncLocal override on the polling provider.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantConfigReaderTests
{
    private const string App = "ReaderApp";
    private const string Env = "Prod";
    private const string TenantAcme = "Acme";
    private const string TenantGlobex = "Globex";

    private sealed class StripeOptions
    {
#pragma warning disable S3459 // Property setters are used by IConfiguration.Bind via reflection.
#pragma warning disable S1144
        public string? Key { get; set; }

        public string? WebhookSecret { get; set; }
#pragma warning restore S1144
#pragma warning restore S3459
    }

    private sealed class PaymentOptions
    {
#pragma warning disable S3459
#pragma warning disable S1144
        public string? Provider { get; set; }

        public int? TimeoutSeconds { get; set; }
#pragma warning restore S1144
#pragma warning restore S3459
    }

    private static async Task<IHost> BuildHostAsync(
        InMemoryConfigStore store,
        ITenantResolver resolver,
        Action<IServiceCollection, IConfiguration>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IConfigStore>(store);
        builder.Services.AddSingleton(resolver);

        var options = new DbConfigOptions
        {
            Scope = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        var source = new DbConfigConfigurationSource(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);

        ((IConfigurationBuilder)builder.Configuration).Add(source);

        builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection("Stripe"));
        builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payment:Stripe"));

        configure?.Invoke(builder.Services, builder.Configuration);

        // Wire the reader manually (mirrors what AddDbConfig does in production).
        var provider = source.Provider
            ?? throw new InvalidOperationException("provider not built");
        builder.Services.AddSingleton<ITenantConfigReader>(sp =>
            new TenantConfigReader(provider, sp.GetRequiredService<IServiceScopeFactory>()));

        var host = builder.Build();
        provider.HostServiceProvider = host.Services;

        return await Task.FromResult(host);
    }

    [TimedFact]
    public async Task GetForTenant_ReturnsTenantValues_WhenTenantHasOverride()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:WebhookSecret", "acme-secret", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);

        var reader = host.Services.GetRequiredService<ITenantConfigReader>();
        var stripe = reader.GetForTenant<StripeOptions>(TenantAcme);

        stripe.Key.ShouldBe("acme-key");
        stripe.WebhookSecret.ShouldBe("acme-secret");
    }

    [TimedFact]
    public async Task GetForTenant_FallsBackToGlobal_WhenTenantLacksKey()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:WebhookSecret", "global-secret", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);

        var reader = host.Services.GetRequiredService<ITenantConfigReader>();
        var stripe = reader.GetForTenant<StripeOptions>(TenantAcme);

        stripe.Key.ShouldBe("acme-key");
        stripe.WebhookSecret.ShouldBe("global-secret");
    }

    [TimedFact]
    public async Task GetForTenant_HonorsCustomSectionPath()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // services.Configure<PaymentOptions>(...GetSection("Payment:Stripe"))
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Payment:Stripe:Provider", "global-provider", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Payment:Stripe:Provider", "acme-provider", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Payment:Stripe:TimeoutSeconds", "60", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);

        var reader = host.Services.GetRequiredService<ITenantConfigReader>();
        var payment = reader.GetForTenant<PaymentOptions>(TenantAcme);

        payment.Provider.ShouldBe("acme-provider");
        payment.TimeoutSeconds.ShouldBe(60);
    }

    [TimedFact]
    public async Task GetForTenant_DoesNotLeakOverride_ToAmbientConfigurationAfterReturn()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);
        var reader = host.Services.GetRequiredService<ITenantConfigReader>();
        var config = host.Services.GetRequiredService<IConfiguration>();

        // Read for Acme via reader.
        var acmeStripe = reader.GetForTenant<StripeOptions>(TenantAcme);
        acmeStripe.Key.ShouldBe("acme-key");

        // Ambient IConfiguration after the call uses the resolver (still returns null).
        config["Stripe:Key"].ShouldBe("global-key");
    }

    [TimedFact]
    public async Task GetForTenant_DoesNotShadowAmbientResolver_InsideOuterRequestScope()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantGlobex, "Stripe:Key", "globex-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = TenantGlobex };
        using var host = await BuildHostAsync(store, resolver);
        var reader = host.Services.GetRequiredService<ITenantConfigReader>();

        // Inside a request scope where resolver returns Globex, read for Acme via reader.
        await using var requestScope = host.Services.CreateAsyncScope();
        var requestSnapshot = requestScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
        requestSnapshot.Value.Key.ShouldBe("globex-key");

        // The reader returns Acme regardless of the ambient resolver.
        var acmeStripe = reader.GetForTenant<StripeOptions>(TenantAcme);
        acmeStripe.Key.ShouldBe("acme-key");

        // Re-resolving in the same scope still sees Globex (resolver, not override).
        var requestSnapshot2 = requestScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
        requestSnapshot2.Value.Key.ShouldBe("globex-key");
    }

    [TimedFact]
    public async Task GetForTenant_ConcurrentCalls_DoNotInterfere()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantGlobex, "Stripe:Key", "globex-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);
        var reader = host.Services.GetRequiredService<ITenantConfigReader>();

        var tasks = Enumerable.Range(0, 50).Select(i =>
            Task.Run(
                () =>
                {
                    var tenant = i % 2 == 0 ? TenantAcme : TenantGlobex;
                    var expected = string.Equals(tenant, TenantAcme, StringComparison.Ordinal) ? "acme-key" : "globex-key";
                    var stripe = reader.GetForTenant<StripeOptions>(tenant);
                    stripe.Key.ShouldBe(expected);
                },
                ct)).ToArray();

        await Task.WhenAll(tasks);
    }

    [TimedFact]
    public async Task GetForTenant_NullTenantId_Throws()
    {
        var store = new InMemoryConfigStore();
        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);
        var reader = host.Services.GetRequiredService<ITenantConfigReader>();

        Should.Throw<ArgumentNullException>(() => reader.GetForTenant<StripeOptions>(null!));
    }

    [TimedFact]
    public async Task GetForTenant_EmptyTenantId_ReadsGlobal()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);
        var reader = host.Services.GetRequiredService<ITenantConfigReader>();

        // Empty string == global tenant (same convention as the data column).
        var globalStripe = reader.GetForTenant<StripeOptions>(string.Empty);
        globalStripe.Key.ShouldBe("global-key");
    }

    [TimedFact]
    public async Task GetForTenant_RunsPostConfigureDelegates()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver, (services, _) =>
            services.PostConfigure<StripeOptions>(opts => opts.WebhookSecret = "post-configure-secret"));

        var reader = host.Services.GetRequiredService<ITenantConfigReader>();
        var stripe = reader.GetForTenant<StripeOptions>(TenantAcme);

        stripe.Key.ShouldBe("acme-key");
        stripe.WebhookSecret.ShouldBe("post-configure-secret");
    }

    [TimedFact]
    public async Task GetForTenant_MatchesIOptionsSnapshot_ForSameTenant()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "Stripe:WebhookSecret", "global-secret", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = TenantAcme };
        using var host = await BuildHostAsync(store, resolver);

        // What IOptionsSnapshot binds for Acme inside a request scope.
        await using var scope = host.Services.CreateAsyncScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
        var fromSnapshot = snapshot.Value;

        // What the reader returns for the same tenant id.
        var reader = host.Services.GetRequiredService<ITenantConfigReader>();
        var fromReader = reader.GetForTenant<StripeOptions>(TenantAcme);

        fromReader.Key.ShouldBe(fromSnapshot.Key);
        fromReader.WebhookSecret.ShouldBe(fromSnapshot.WebhookSecret);
    }
}
