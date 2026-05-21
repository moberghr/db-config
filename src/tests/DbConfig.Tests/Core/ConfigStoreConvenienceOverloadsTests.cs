using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests for the v0.11.1 convenience overloads on <see cref="IConfigStore"/> — implicit
/// Scope/Environment via <see cref="DbConfigOptions"/>, current-tenant lookup via
/// <see cref="ITenantResolver"/>, and typed POCO binders with verbatim type-name section.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConfigStoreConvenienceOverloadsTests
{
    private const string App = "ConvenienceApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task GetAsync_NoResolver_ReturnsGlobalEntry()
    {
        var (store, options) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Logging:Level", "Info", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetAsync("Logging:Level", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("Info");
        result.TenantId.ShouldBe(string.Empty);
        options.Scope.ShouldBe(App);
    }

    [TimedFact]
    public async Task GetAsync_WithResolver_PrefersTenantEntry()
    {
        var resolver = new MutableTenantResolver("Acme");
        var (store, _) = CreateStore(resolver);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Feature:Beta", "false", false, t, "seed"),
            CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "Feature:Beta", "true", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetAsync("Feature:Beta", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("true");
        result.TenantId.ShouldBe("Acme");
    }

    [TimedFact]
    public async Task GetAsync_WithResolverNullTenant_ReturnsGlobal()
    {
        var resolver = new MutableTenantResolver(tenant: null);
        var (store, _) = CreateStore(resolver);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Locale", "en-US", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetAsync("Locale", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("en-US");
        result.TenantId.ShouldBe(string.Empty);
    }

    [TimedFact]
    public async Task GetForTenantAsync_ImplicitAppEnv_ReturnsTenantEntry()
    {
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Globex", "Limits:Max", "100000", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetForTenantAsync("Globex", "Limits:Max", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("100000");
        result.TenantId.ShouldBe("Globex");
    }

    [TimedFact]
    public async Task GetAsync_Typed_BindsCurrentTenantValues()
    {
        var resolver = new MutableTenantResolver("Acme");
        var (store, _) = CreateStore(resolver);
        var t = DateTimeOffset.UtcNow;

        // Section name = typeof(T).Name verbatim → "StripeOptions:"
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "StripeOptions:ApiKey", "global-key", false, t, "seed"),
            CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "StripeOptions:DefaultCurrency", "USD", false, t, "seed"),
            CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "StripeOptions:ApiKey", "acme-key", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetAsync<StripeOptions>(CancellationToken.None);

        result.ShouldNotBeNull();
        result.ApiKey.ShouldBe("acme-key");
        result.DefaultCurrency.ShouldBe("USD");
    }

    [TimedFact]
    public async Task GetForTenantAsync_Typed_PrefersTenantOverGlobal()
    {
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "StripeOptions:ApiKey", "g", false, t, "seed"),
            CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "StripeOptions:ApiKey", "a", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetForTenantAsync<StripeOptions>("Acme", CancellationToken.None);

        result.ApiKey.ShouldBe("a");
    }

    [TimedFact]
    public async Task GetForTenantAsync_Typed_FallsBackToGlobalForMissingTenantKeys()
    {
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "StripeOptions:ApiKey", "global-key", false, t, "seed"),
            CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "StripeOptions:WebhookSecret", "global-webhook", false, t, "seed"),
            CancellationToken.None);
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "StripeOptions:ApiKey", "acme-key", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetForTenantAsync<StripeOptions>("Acme", CancellationToken.None);

        result.ApiKey.ShouldBe("acme-key", "tenant value should override global");
        result.WebhookSecret.ShouldBe("global-webhook", "global value passes through for missing tenant key");
    }

    [TimedFact]
    public async Task GetForTenantAsync_Typed_DecryptsSecrets()
    {
        var encryptor = new ReversibleTestEncryptor();
        var (store, _) = CreateStore(resolver: null, encryptor: encryptor);
        var t = DateTimeOffset.UtcNow;

        // The store will encrypt at-rest via the encryptor; the typed bind must return plaintext.
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "StripeOptions:ApiKey", "sk_live_secret", true, t, "seed"),
            CancellationToken.None);

        var result = await store.GetForTenantAsync<StripeOptions>("Acme", CancellationToken.None);

        result.ApiKey.ShouldBe("sk_live_secret");
    }

    [TimedFact]
    public async Task GetForTenantAsync_Typed_SectionNameIsTypeNameVerbatim()
    {
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;

        // Verbatim type name → only "StripeSettings:" prefix is honored.
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "StripeSettings:ApiKey", "from-settings", false, t, "seed"),
            CancellationToken.None);

        // "Stripe:" entries should NOT be bound — different prefix.
        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Stripe:ApiKey", "from-stripe-prefix", false, t, "seed"),
            CancellationToken.None);

        var result = await store.GetAsync<StripeSettings>(CancellationToken.None);

        result.ApiKey.ShouldBe("from-settings");
    }

    [TimedFact]
    public async Task GetAllAsync_ImplicitAppEnv_ReturnsExpectedEntries()
    {
        var resolver = new MutableTenantResolver("Acme");
        var (store, _) = CreateStore(resolver);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "K1", "g1", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "K2", "g2", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, "Acme", "K1", "a1", false, t, "seed"), ct);

        var result = await store.GetAllAsync(ct);

        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThan(0);
    }

    [TimedFact]
    public async Task GetAllForTenantAsync_ImplicitAppEnv_ReturnsOnlyTenantEntries()
    {
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "GlobalKey", "g", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, "Globex", "TenantKey1", "v1", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, "Globex", "TenantKey2", "v2", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, "Acme", "OtherTenant", "x", false, t, "seed"), ct);

        var result = await store.GetAllForTenantAsync("Globex", ct);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(e => e.TenantId == "Globex");
    }

    [TimedFact]
    public async Task GetForTenantAsync_Typed_NoMatchingKeys_ReturnsDefaultValues()
    {
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Seed an unrelated key — section "StripeOptions" has no matches.
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Unrelated:Key", "x", false, t, "seed"), ct);

        var result = await store.GetForTenantAsync<StripeOptions>("Acme", ct);

        result.ShouldNotBeNull();
        result.ApiKey.ShouldBe(string.Empty);            // default
        result.WebhookSecret.ShouldBe(string.Empty);     // default
        result.DefaultCurrency.ShouldBe("USD");          // POCO's default
    }

    [TimedFact]
    public async Task CustomStore_AmbientStubThrows_PropagatesNotSupportedException()
    {
        // The v0.14.0 ISP split removed default-throwing interface methods from IConfigStore.
        // A custom store that opts out of the ambient (current-tenant) read contract now
        // declares that intent by throwing from its IAmbientConfigReader implementations
        // — this test verifies the throw surfaces correctly to callers.
        IConfigStore store = new StubExplicitOnlyStore();

        await Should.ThrowAsync<NotSupportedException>(
            async () => await store.GetAsync("anything", CancellationToken.None));
    }

    [TimedFact]
    public async Task GetForTenantAsync_Typed_StripsGenericArity()
    {
        // typeof(MyGeneric<int>).Name is "MyGeneric`1" — the bind must strip the arity
        // so keys prefixed "MyGeneric:" are matched (NOT "MyGeneric`1:").
        var (store, _) = CreateStore(resolver: null);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, "Acme", "MyGeneric:Label", "from-generic", false, t, "seed"),
            ct);

        var result = await store.GetForTenantAsync<MyGeneric<int>>("Acme", ct);

        result.Label.ShouldBe("from-generic");
    }

    [TimedFact]
    public async Task GetAsync_WhitespaceResolver_FallsBackToGlobal()
    {
        // ITenantResolver returning whitespace should be treated as null/empty —
        // we should NOT issue a literal-tenant lookup for "   ".
        var resolver = new MutableTenantResolver("   ");
        var (store, _) = CreateStore(resolver);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Locale", "en-US", false, t, "seed"),
            ct);

        var result = await store.GetAsync("Locale", ct);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe("en-US");
        result.TenantId.ShouldBe(string.Empty);
    }

    [TimedFact]
    public async Task BindTypedAsync_UsesQueryAsync_NotFullScan()
    {
        // The v0.11.1 review identified that BindTypedAsync was issuing two full-scope
        // scans (GetAllAsync + GetAllForTenantAsync) and filtering in-memory. The fix
        // routes through QueryAsync with a keyPrefix filter. Verify by asserting the
        // counters: zero full-scope calls, at least one QueryAsync call.
        var resolver = new MutableTenantResolver("Acme");
        var (store, _) = CreateStore(resolver);
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        // Seed both global and tenant section keys plus an unrelated key.
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "StripeOptions:ApiKey", "g", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, "Acme", "StripeOptions:ApiKey", "a", false, t, "seed"), ct);
        await store.UpsertAsync(new ConfigEntryRecord(App, Env, string.Empty, "Unrelated:Key", "x", false, t, "seed"), ct);

        // Reset counters AFTER seeding so we measure only the typed-bind read path.
        var beforeGetAll = store.GetAllAsyncCallCount;
        var beforeGetAllForTenant = store.GetAllForTenantAsyncCallCount;
        var beforeQuery = store.QueryAsyncCallCount;

        var result = await store.GetAsync<StripeOptions>(ct);

        result.ApiKey.ShouldBe("a");
        store.GetAllAsyncCallCount.ShouldBe(beforeGetAll, "typed bind must not trigger a full-scope global scan");
        store.GetAllForTenantAsyncCallCount.ShouldBe(beforeGetAllForTenant, "typed bind must not trigger a full-scope tenant scan");
        store.QueryAsyncCallCount.ShouldBeGreaterThan(beforeQuery, "typed bind should issue at least one QueryAsync with the section prefix");
    }

    [TimedFact]
    public async Task InMemoryStore_NullOptions_ConvenienceMethodThrowsHelpfully()
    {
        // No DbConfigOptions passed in → implicit app/env path cannot resolve.
        // Documented contract: throws InvalidOperationException with a clear hint.
        var store = new InMemoryConfigStore(
            encryptor: null,
            auditStore: null,
            enableAuditLog: false,
            options: null,
            tenantResolver: null);

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "K", "v", false, DateTimeOffset.UtcNow, "seed"),
            CancellationToken.None);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.GetAsync("K", CancellationToken.None));

        ex.Message.ShouldContain("DbConfigOptions", Case.Insensitive);
    }

    private sealed class StubExplicitOnlyStore : IConfigStore
    {
        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(string scope, string environment, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ConfigEntryRecord>>([]);

        public Task<ConfigEntryRecord?> GetAsync(string scope, string environment, string key, CancellationToken ct)
            => Task.FromResult<ConfigEntryRecord?>(null);

        public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task UpsertAsync(ConfigEntryRecord entry, CancellationToken ct) => Task.CompletedTask;

        public Task DeleteAsync(string scope, string environment, string key, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedAsync(IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ConfigEntryRecord>>([]);

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string scope, string environment, string tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ConfigEntryRecord>>([]);

        public Task<ConfigEntryRecord?> GetForTenantAsync(string scope, string environment, string tenantId, string key, CancellationToken ct)
            => Task.FromResult<ConfigEntryRecord?>(null);

        public Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(string scope, string environment, string tenantId, CancellationToken ct)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task DeleteForTenantAsync(string scope, string environment, string tenantId, string key, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(string scope, string environment, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ConfigEntryRecord>>([]);

        public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(string scope, string environment, CancellationToken ct)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ConfigEntryRecord>>([]);

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task<IReadOnlyList<ConfigEntryRecord>> QueryAsync(string? scope, string? environment, string? tenantId, string? keyPrefix, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ConfigEntryRecord>>([]);

        // Ambient (current-tenant) reads are explicitly unsupported by this stub.
        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(CancellationToken ct)
            => throw new NotSupportedException("StubExplicitOnlyStore does not support ambient GetAllAsync.");

        public Task<ConfigEntryRecord?> GetAsync(string key, CancellationToken ct)
            => throw new NotSupportedException("StubExplicitOnlyStore does not support ambient GetAsync.");

        public Task<T> GetAsync<T>(CancellationToken ct)
            where T : class, new()
            => throw new NotSupportedException("StubExplicitOnlyStore does not support typed GetAsync<T>.");

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
            => throw new NotSupportedException("StubExplicitOnlyStore does not support ambient GetAllForTenantAsync.");

        public Task<ConfigEntryRecord?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
            => throw new NotSupportedException("StubExplicitOnlyStore does not support ambient GetForTenantAsync.");

        public Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
            where T : class, new()
            => throw new NotSupportedException("StubExplicitOnlyStore does not support typed GetForTenantAsync<T>.");
    }

    private static (InMemoryConfigStore Store, DbConfigOptions Options) CreateStore(
        ITenantResolver? resolver,
        IConfigEncryptor? encryptor = null)
    {
        var options = new DbConfigOptions
        {
            Scope = App,
            Environment = Env,
        };

        var store = new InMemoryConfigStore(
            encryptor: encryptor,
            auditStore: null,
            enableAuditLog: false,
            options: options,
            tenantResolver: resolver);

        return (store, options);
    }

    private sealed class StripeOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        public string WebhookSecret { get; set; } = string.Empty;

        public string DefaultCurrency { get; set; } = "USD";
    }

    private sealed class StripeSettings
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    private sealed class MyGeneric<T>
    {
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// Test encryptor that wraps values with a stable, reversible prefix so we can detect
    /// the typed binder is decrypting before bind (otherwise <c>ApiKey</c> would equal the
    /// wrapped ciphertext, not the original plaintext).
    /// </summary>
    private sealed class ReversibleTestEncryptor : IConfigEncryptor
    {
        private const string Marker = "ENC::";

        public string Protect(string plaintext) => Marker + plaintext;

        public string Unprotect(string ciphertext)
        {
            if (!ciphertext.StartsWith(Marker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Not a protected payload.");
            }

            return ciphertext[Marker.Length..];
        }
    }
}
