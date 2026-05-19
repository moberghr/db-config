using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests for the tenant-aware <c>TryGet</c> on <see cref="DbConfigConfigurationProvider"/>
/// via <see cref="ITenantResolver"/>. Covers resolver registration, global fallback semantics,
/// secret decryption per tenant, and the DI registration shape of
/// <see cref="DbConfigBuilder.AddTenantResolver{TResolver}"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantResolverTryGetTests
{
    private const string App = "ResolverApp";
    private const string Env = "Test";

    // A valid-looking but non-functional SQL Server connection string.
    private const string FakeConnectionString =
        "Server=127.0.0.1,19999;Database=test;User Id=sa;Password=fake;Connect Timeout=1;Encrypt=false;";

    private sealed class FakeResolver : ITenantResolver
    {
        public string? Tenant { get; set; }

        public string? Resolve() => Tenant;
    }

    private sealed class PrefixEncryptor : IConfigEncryptor
    {
        private readonly string _prefix;

        public PrefixEncryptor(string prefix) => _prefix = prefix;

        public string Protect(string plaintext) => _prefix + plaintext;

        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith(_prefix, StringComparison.Ordinal)
                ? ciphertext[_prefix.Length..]
                : ciphertext;
    }

    private static DbConfigConfigurationProvider CreateProvider(IConfigStore store)
    {
        var options = new DbConfigOptions
        {
            AppName = App,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
        };

        return new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    private static ServiceProvider BuildServiceProvider(ITenantResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        return services.BuildServiceProvider();
    }

    [TimedFact]
    public async Task TryGet_NoResolverRegistered_ReturnsGlobalEntry()
    {
        var store = new InMemoryConfigStore();
        var provider = CreateProvider(store);
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "App:Name", "global-app", false, now, null),
            TestContext.Current.CancellationToken);

        provider.Load();

        // No HostServiceProvider set → NullTenantResolver → global fallback.
        provider.TryGet("App:Name", out var value).ShouldBeTrue();
        value.ShouldBe("global-app");
    }

    [TimedFact]
    public async Task TryGet_ResolverReturnsNull_ReturnsGlobalEntry()
    {
        var store = new InMemoryConfigStore();
        var provider = CreateProvider(store);
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "Feature:X", "global-x", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntry(App, Env, "Acme", "Feature:X", "acme-x", false, now.AddSeconds(1), null),
            TestContext.Current.CancellationToken);

        provider.Load();

        var resolver = new FakeResolver { Tenant = null };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("Feature:X", out var value).ShouldBeTrue();
        value.ShouldBe("global-x");
    }

    [TimedFact]
    public async Task TryGet_ResolverReturnsTenant_ReturnsTenantEntry()
    {
        var store = new InMemoryConfigStore();
        var provider = CreateProvider(store);
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "Stripe:ApiKey", "global-key", false, now, null),
            TestContext.Current.CancellationToken);
        await store.UpsertAsync(
            new ConfigEntry(App, Env, "Acme", "Stripe:ApiKey", "acme-key", false, now.AddSeconds(1), null),
            TestContext.Current.CancellationToken);

        provider.Load();

        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("Stripe:ApiKey", out var value).ShouldBeTrue();
        value.ShouldBe("acme-key");
    }

    [TimedFact]
    public async Task TryGet_ResolverReturnsTenant_MissingTenantEntry_FallsBackToGlobal()
    {
        var store = new InMemoryConfigStore();
        var provider = CreateProvider(store);
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "Smtp:Host", "global-smtp", false, now, null),
            TestContext.Current.CancellationToken);

        // Globex has no override for Smtp:Host
        provider.Load();

        var resolver = new FakeResolver { Tenant = "Globex" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("Smtp:Host", out var value).ShouldBeTrue();
        value.ShouldBe("global-smtp");
    }

    [TimedFact]
    public async Task TryGet_ResolverReturnsTenant_MissingBothEntries_ReturnsFalse()
    {
        var store = new InMemoryConfigStore();
        var provider = CreateProvider(store);
        var now = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "Other:Key", "other-val", false, now, null),
            TestContext.Current.CancellationToken);

        provider.Load();

        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("DoesNotExist", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [TimedFact]
    public async Task TryGet_TenantSecretEntry_Decrypted()
    {
        var encryptor = new PrefixEncryptor("ENC:");
        var rawStore = new InMemoryConfigStore(encryptor: null);
        var provider = CreateProvider(rawStore);
        var now = DateTimeOffset.UtcNow;

        await rawStore.UpsertAsync(
            new ConfigEntry(App, Env, "Acme", "Payment:SecretKey", encryptor.Protect("acme-secret"), true, now, null),
            TestContext.Current.CancellationToken);

        provider.Load();
        provider.SetEncryptor(encryptor);

        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        provider.TryGet("Payment:SecretKey", out var value).ShouldBeTrue();
        value.ShouldBe("acme-secret");
    }

    [TimedFact]
    public async Task TryGet_TenantSecretEntry_BeforeEncryptorSet_Throws()
    {
        var encryptor = new PrefixEncryptor("ENC:");
        var rawStore = new InMemoryConfigStore(encryptor: null);
        var provider = CreateProvider(rawStore);
        var now = DateTimeOffset.UtcNow;

        await rawStore.UpsertAsync(
            new ConfigEntry(App, Env, "Acme", "Db:Password", encryptor.Protect("secret-pw"), true, now, null),
            TestContext.Current.CancellationToken);

        provider.Load();

        // Encryptor NOT set — should throw on read.
        var resolver = new FakeResolver { Tenant = "Acme" };
        await using var sp = BuildServiceProvider(resolver);
        provider.HostServiceProvider = sp;

        var ex = Should.Throw<InvalidOperationException>(() => provider.TryGet("Db:Password", out _));
        ex.Message.ShouldContain("host.Build()");
    }

    [Fact]
    public void AddTenantResolver_RegistersAsSingleton()
    {
        var builder = WebApplication.CreateSlimBuilder();

        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.AppName = App;
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
                b.AddTenantResolver<FakeResolver>();
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() throws for unreachable DB.
        }

        var descriptor = builder.Services.FirstOrDefault(
            d => d.ServiceType == typeof(ITenantResolver));

        descriptor.ShouldNotBeNull();
        descriptor!.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        descriptor.ImplementationType.ShouldBe(typeof(FakeResolver));
    }
}
