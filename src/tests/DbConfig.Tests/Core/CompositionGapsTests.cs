using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Reviewer-identified composition gaps not covered by the headline 32-case precedence
/// matrix. Each test exercises a specific axis combination (multi-tenant coexistence,
/// tenant-vs-scope priority, empty-string handling, missing/throwing resolver,
/// case sensitivity, reload across tenant axis, foreign-app filter, and IsSecret +
/// tenant composition).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompositionGapsTests
{
    private const string OwnApp = "MyApp";
    private const string SharedApp = "Shared";
    private const string Env = "Prod";

    private static DbConfigConfigurationProvider CreateProvider(
        IConfigStore store,
        string appName = OwnApp,
        string env = Env,
        string[]? includeScopes = null,
        TimeProvider? timeProvider = null)
    {
        var options = new DbConfigOptions
        {
            AppName = appName,
            Environment = env,
            ReloadInterval = TimeSpan.FromSeconds(30),
            IncludeScopes = includeScopes ?? [],
        };

        return new DbConfigConfigurationProvider(
            options,
            store,
            timeProvider ?? TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    private static ServiceProvider BuildSp(ITenantResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resolver);

        return services.BuildServiceProvider();
    }

    [TimedFact]
    public async Task MultipleTenants_CoexistInSameProvider_FlippingResolverPicksCorrectBag()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "K", "acme-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Globex", "K", "globex-v", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        var resolver = new MutableTenantResolver();
        var sp = BuildSp(resolver);
        provider.HostServiceProvider = sp;

        resolver.Tenant = null;
        provider.TryGet("K", out var v1).ShouldBeTrue();
        v1.ShouldBe("global-v");

        resolver.Tenant = "Acme";
        provider.TryGet("K", out var v2).ShouldBeTrue();
        v2.ShouldBe("acme-v");

        resolver.Tenant = "Globex";
        provider.TryGet("K", out var v3).ShouldBeTrue();
        v3.ShouldBe("globex-v");

        resolver.Tenant = "Stranger";
        provider.TryGet("K", out var v4).ShouldBeTrue();
        v4.ShouldBe("global-v");
    }

    [TimedFact]
    public async Task TenantAxis_BeatsScopeAxis_EvenWhenTenantRowIsInLowerScope()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-myapp", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(SharedApp, Env, "Acme", "K", "acme-shared", false, t, null), ct);

        var provider = CreateProvider(store, includeScopes: [SharedApp]);
        provider.Load();

        var resolver = new MutableTenantResolver("Acme");
        provider.HostServiceProvider = BuildSp(resolver);

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("acme-shared");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceTenant_FallsBackToGlobal(string resolverValue)
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "K", "acme-v", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        var resolver = new MutableTenantResolver(resolverValue);
        provider.HostServiceProvider = BuildSp(resolver);

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("global-v");
    }

    [TimedFact]
    public async Task NoTenantResolverRegistered_FallsBackToNullResolver_ReturnsGlobal()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "K", "acme-v", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        // ServiceProvider with NO ITenantResolver registration.
        var services = new ServiceCollection();
        await using var sp = services.BuildServiceProvider();
        provider.HostServiceProvider = sp;

        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("global-v");
    }

    [TimedFact]
    public async Task HostServiceProviderNotSet_ReturnsGlobal_WithoutThrowing()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-v", false, t, null), ct);
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "K", "acme-v", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        // Intentionally do NOT set HostServiceProvider (pre-build / no-SP path).
        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("global-v");
    }

    [TimedFact]
    public async Task TenantIds_AreCaseSensitive()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Only an "Acme" (PascalCase) row exists; no global, no lowercase row.
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "K", "acme-cap", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        var resolver = new MutableTenantResolver();
        provider.HostServiceProvider = BuildSp(resolver);

        // Lowercase miss — no matching row, no global fallback.
        resolver.Tenant = "acme";
        provider.TryGet("K", out var lower).ShouldBeFalse();
        lower.ShouldBeNull();

        // Exact case hit.
        resolver.Tenant = "Acme";
        provider.TryGet("K", out var upper).ShouldBeTrue();
        upper.ShouldBe("acme-cap");
    }

    [TimedFact]
    public async Task Reload_PicksUpNewlyAddedTenantRow_AfterWatermarkAdvance()
    {
        var fakeTime = new FakeTimeProvider();
        var store = new InMemoryConfigStore();
        var t0 = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-v", false, t0, null), ct);

        var provider = CreateProvider(store, timeProvider: fakeTime);
        provider.Load();

        var resolver = new MutableTenantResolver("Acme");
        provider.HostServiceProvider = BuildSp(resolver);

        // Before reload: Acme falls back to global.
        provider.TryGet("K", out var before).ShouldBeTrue();
        before.ShouldBe("global-v");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.GetReloadToken().RegisterChangeCallback(_ => tcs.TrySetResult(true), null);

        // Add a tenant-specific row with a later timestamp.
        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "K", "acme-new", false, t0.AddMinutes(1), null), ct);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        provider.TryGet("K", out var after).ShouldBeTrue();
        after.ShouldBe("acme-new");
    }

    [TimedFact]
    public async Task ForeignAppWrite_DoesNotAdvanceWatermark_NorTriggerReload()
    {
        var fakeTime = new FakeTimeProvider();
        var store = new InMemoryConfigStore();
        var t0 = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "myapp-v", false, t0, null), ct);
        await store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, "K", "shared-v", false, t0, null), ct);

        var provider = CreateProvider(store, includeScopes: [SharedApp], timeProvider: fakeTime);
        provider.Load();

        var reloadFired = false;
        provider.GetReloadToken().RegisterChangeCallback(_ => reloadFired = true, null);

        // Foreign app (not in scope list) writes a much later row.
        await store.UpsertAsync(new ConfigEntry("Foreign", Env, string.Empty, "K", "foreign-v", false, t0.AddMinutes(10), null), ct);

        fakeTime.Advance(TimeSpan.FromSeconds(30));

        await Task.Delay(50, ct);

        reloadFired.ShouldBeFalse();
        provider.TryGet("K", out var value).ShouldBeTrue();
        value.ShouldBe("myapp-v");
    }

    [TimedFact]
    public async Task IsSecretTenantKey_RoundTripsThroughResolutionChain()
    {
        // Composition test: an IsSecret=true entry scoped to a tenant must roundtrip plaintext
        // through the store → provider → TryGet path. The encryption seam itself is covered
        // by TypeMappedEncryptorTests; here we only assert that the IsSecret + tenant axes
        // compose correctly so the provider's _isSecretByTenantKey path is exercised end-to-end.
        //
        // Production layering (post-fix): the polling-side store has NO encryptor (passthrough),
        // so it returns raw ciphertext into _tenantData; the provider holds the encryptor and
        // decrypts in TryGet. We mirror that here by writing through a "writer" store that has
        // the encryptor, then reading via a "raw" store with no encryptor — same shape as the
        // production polling pipeline.
        var encryptor = new TestEncryptor();
        var writerStore = new InMemoryConfigStore(encryptor);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await writerStore.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "Stripe:Key", "secret-acme", true, t, null), ct);
        await writerStore.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "Stripe:Key", "secret-global", true, t, null), ct);

        // Read-side store mirrors the polling pipeline: passthrough encryptor (null →
        // Passthrough internally), so _tenantData receives ciphertext that the provider
        // will decrypt on TryGet.
        var rawCiphertextAcme = encryptor.Protect("secret-acme");
        var rawCiphertextGlobal = encryptor.Protect("secret-global");
        var readerStore = new InMemoryConfigStore(encryptor: null);
        await readerStore.UpsertAsync(new ConfigEntry(OwnApp, Env, "Acme", "Stripe:Key", rawCiphertextAcme, true, t, null), ct);
        await readerStore.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "Stripe:Key", rawCiphertextGlobal, true, t, null), ct);

        var provider = CreateProvider(readerStore);
        provider.Load();
        provider.SetEncryptor(encryptor);

        var resolver = new MutableTenantResolver("Acme");
        provider.HostServiceProvider = BuildSp(resolver);

        provider.TryGet("Stripe:Key", out var acmeValue).ShouldBeTrue();
        acmeValue.ShouldBe("secret-acme");

        // Flip to global — should decrypt the global ciphertext to plaintext.
        resolver.Tenant = null;
        provider.TryGet("Stripe:Key", out var globalValue).ShouldBeTrue();
        globalValue.ShouldBe("secret-global");
    }

    [TimedFact]
    public async Task ResolverThrowing_PropagatesException()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, "K", "global-v", false, t, null), ct);

        var provider = CreateProvider(store);
        provider.Load();

        provider.HostServiceProvider = BuildSp(new ThrowingResolver());

        // Locks the current behaviour: a throwing resolver propagates to the caller. If we
        // ever change to catch-and-treat-as-null, this test fails loudly and forces a
        // deliberate decision.
        Should.Throw<InvalidOperationException>(() => provider.TryGet("K", out _));
    }

    /// <summary>
    /// Strict test encryptor: <see cref="Unprotect"/> THROWS when handed input that does
    /// not look like ciphertext produced by <see cref="Protect"/>. This catches double-
    /// decrypt regressions where plaintext leaks into the path that expects ciphertext —
    /// see tasks/encryption-layering-audit.md for the original bug this guards against.
    /// </summary>
    private sealed class TestEncryptor : IConfigEncryptor
    {
        public string Protect(string plaintext) => "ENC:" + plaintext;

        public string Unprotect(string ciphertext)
        {
            if (!ciphertext.StartsWith("ENC:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"TestEncryptor.Unprotect received non-ciphertext input '{ciphertext}'. " +
                    "This indicates a double-decrypt or layering bug — the value reached the " +
                    "decryptor already in plaintext.");
            }

            return ciphertext["ENC:".Length..];
        }
    }

    private sealed class ThrowingResolver : ITenantResolver
    {
        public string? Resolve() => throw new InvalidOperationException("boom");
    }
}
