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
/// Verifies that <see cref="IOptionsSnapshot{T}"/> binds with the current tenant context
/// per request scope, while <see cref="IOptions{T}"/> caches at startup and never updates.
/// Documents the IOptions vs IOptionsSnapshot contract (CLAUDE.md §0.8, architecture.md §2.15).
/// </summary>
[Trait("Category", "Unit")]
public sealed class OptionsBindingTests
{
    private const string App = "OptionsApp";
    private const string Env = "Prod";
    private const string TenantAcme = "Acme";

    private sealed class StripeOptions
    {
#pragma warning disable S3459 // Property setters are used by IConfiguration.Bind via reflection.
#pragma warning disable S1144
        public string? Key { get; set; }

        public string? WebhookSecret { get; set; }
#pragma warning restore S1144
#pragma warning restore S3459
    }

    private static async Task<IHost> BuildHostAsync(InMemoryConfigStore store, ITenantResolver resolver)
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

        var host = builder.Build();

        // The source built the provider during configuration construction; wire host DI
        // so the provider can resolve ITenantResolver from request scopes.
        if (source.Provider is not null)
        {
            source.Provider.HostServiceProvider = host.Services;
        }

        return await Task.FromResult(host);
    }

    [TimedFact]
    public async Task IOptionsSnapshot_TracksCurrentTenant_PerScope()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver();
        using var host = await BuildHostAsync(store, resolver);

        // First scope: tenant is Acme.
        resolver.Tenant = TenantAcme;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
            snapshot.Value.Key.ShouldBe("acme-key");
        }

        // Second scope: tenant is null (global).
        resolver.Tenant = null;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
            snapshot.Value.Key.ShouldBe("global-key");
        }
    }

    [TimedFact]
    public async Task IOptions_CachesAtStartup_AndReturnsGlobalForever()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        // Resolver returns null at startup time — IOptions binds with global values.
        var resolver = new MutableTenantResolver { Tenant = null };
        using var host = await BuildHostAsync(store, resolver);

        var initial = host.Services.GetRequiredService<IOptions<StripeOptions>>();
        initial.Value.Key.ShouldBe("global-key");

        // Even after the resolver flips to Acme, IOptions stays cached on global.
        resolver.Tenant = TenantAcme;
        var afterFlip = host.Services.GetRequiredService<IOptions<StripeOptions>>();
        afterFlip.Value.Key.ShouldBe("global-key");
    }

    [TimedFact]
    public async Task Bind_FallsBackToGlobal_WhenTenantHasNoEntry()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = TenantAcme };
        using var host = await BuildHostAsync(store, resolver);

        await using var scope = host.Services.CreateAsyncScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
        snapshot.Value.Key.ShouldBe("global-key");
    }

    [TimedFact]
    public async Task Bind_PrefersTenantValues_WhenBothPresent()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = TenantAcme };
        using var host = await BuildHostAsync(store, resolver);

        await using var scope = host.Services.CreateAsyncScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
        snapshot.Value.Key.ShouldBe("acme-key");
    }

    [TimedFact]
    public async Task Bind_PartialTenantOverride_FallsBackPerKey()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Global has both Key and WebhookSecret.
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Stripe:Key", "global-key", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Stripe:WebhookSecret", "global-secret", false, t, null), ct);

        // Acme overrides ONLY Key.
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = TenantAcme };
        using var host = await BuildHostAsync(store, resolver);

        await using var scope = host.Services.CreateAsyncScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();

        snapshot.Value.Key.ShouldBe("acme-key");
        snapshot.Value.WebhookSecret.ShouldBe("global-secret");
    }

    [TimedFact]
    public async Task Bind_MissesTenantOnlyKey_WhenGlobalSkeletonAbsent()
    {
        // Documents the known sharp edge: IConfiguration.Bind discovers child keys via the
        // standard Microsoft.Extensions.Configuration enumeration, which (for our provider)
        // walks only the global-tenant Data dictionary. A tenant-only key with no global
        // skeleton is NOT bound onto the POCO.
        // Direct IConfiguration[key] reads DO see tenant-only values (covered elsewhere).
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Only Acme has a Stripe:Key — no global skeleton under "Stripe".
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, TenantAcme, "Stripe:Key", "acme-key", false, t, null), ct);

        var resolver = new MutableTenantResolver { Tenant = TenantAcme };
        using var host = await BuildHostAsync(store, resolver);

        // Direct read finds the tenant-only value.
        var config = host.Services.GetRequiredService<IConfiguration>();
        config["Stripe:Key"].ShouldBe("acme-key");

        // But the bound POCO does not — Bind walks child keys via global Data.
        await using var scope = host.Services.CreateAsyncScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>();
        snapshot.Value.Key.ShouldBeNull();
    }
}
