using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests for the type-mapped <see cref="IConfigEncryptor"/> registration path introduced in v1.4.
/// Verifies that <c>services.AddSingleton&lt;IConfigEncryptor, MyImpl&gt;()</c> works end-to-end:
/// the polling provider stores raw ciphertext until <c>DbConfigEncryptorActivator.StartAsync</c>
/// runs (after host.Build()), at which point secret values are decryptable on demand.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TypeMappedEncryptorTests
{
    private const string App = "TypeMappedApp";
    private const string Env = "Test";

    // A valid-looking but non-functional SQL Server connection string.
    private const string FakeConnectionString =
        "Server=127.0.0.1,19999;Database=test;User Id=sa;Password=fake;Connect Timeout=1;Encrypt=false;";

    // -------------------------------------------------------------------------
    // Direct provider-level tests (no host needed — exercises TryGet behavior)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the polling-side provider has no encryptor set yet and a non-secret key is
    /// read, it returns the raw value without throwing.
    /// </summary>
    [TimedFact]
    public void TypeMappedRegistration_ReadNonSecretBeforeBuild_Succeeds()
    {
        // Store seeded with pre-encrypted data. The polling-side has no encryptor set.
        ConfigEntryRecord[] entries =
        [
            new ConfigEntryRecord(App, Env, string.Empty, "NonSecret:Key", "plain-value", false, DateTimeOffset.UtcNow, null),
            new ConfigEntryRecord(App, Env, string.Empty, "Secret:Key", "ENC:plaintext-secret", true, DateTimeOffset.UtcNow, null),
        ];

        var store = new RawValueStore(entries);
        var options = new DbConfigOptions { Scope = App, Environment = Env };
        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        provider.Load();

        // Non-secret key must be readable before SetEncryptor is called.
        provider.TryGet("NonSecret:Key", out var value).ShouldBeTrue();
        value.ShouldBe("plain-value");
    }

    /// <summary>
    /// When the polling-side provider has no encryptor set yet and a secret key is read,
    /// it throws <see cref="InvalidOperationException"/> with a clear message.
    /// </summary>
    [TimedFact]
    public void TypeMappedRegistration_ReadSecretBeforeBuild_Throws()
    {
        ConfigEntryRecord[] entries =
        [
            new ConfigEntryRecord(App, Env, string.Empty, "Secret:Key", "ENC:plaintext-secret", true, DateTimeOffset.UtcNow, null),
        ];

        var store = new RawValueStore(entries);
        var options = new DbConfigOptions { Scope = App, Environment = Env };
        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        provider.Load();

        // Secret key without encryptor must throw.
        var ex = Should.Throw<InvalidOperationException>(() =>
            provider.TryGet("Secret:Key", out _));

        ex.Message.ShouldContain("Secret:Key");
        ex.Message.ShouldContain("host.Build()");
    }

    /// <summary>
    /// After <see cref="DbConfigConfigurationProvider.SetEncryptor"/> is called (simulating what
    /// <c>DbConfigEncryptorActivator</c> does in StartAsync), reading a secret key returns plaintext.
    /// </summary>
    [TimedFact]
    public void TypeMappedRegistration_AfterSetEncryptor_DecryptsCorrectly()
    {
        var encryptor = new FakeEncryptor();

        ConfigEntryRecord[] entries =
        [
            new ConfigEntryRecord(App, Env, string.Empty, "Secret:Key", encryptor.Protect("original-plaintext"), true, DateTimeOffset.UtcNow, null),
            new ConfigEntryRecord(App, Env, string.Empty, "NonSecret:Key", "visible", false, DateTimeOffset.UtcNow, null),
        ];

        var store = new RawValueStore(entries);
        var options = new DbConfigOptions { Scope = App, Environment = Env };
        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        provider.Load();

        // Inject the encryptor (simulating the activator).
        provider.SetEncryptor(encryptor);

        // Secret key must now decrypt.
        provider.TryGet("Secret:Key", out var secretValue).ShouldBeTrue();
        secretValue.ShouldBe("original-plaintext");

        // Non-secret remains unchanged.
        provider.TryGet("NonSecret:Key", out var nonSecretValue).ShouldBeTrue();
        nonSecretValue.ShouldBe("visible");
    }

    /// <summary>
    /// Verifies that the instance-registration path (v1.3) still works. The encryptor instance
    /// is pre-registered before AddDbConfig; no activator hosted service is registered.
    /// </summary>
    [TimedFact]
    public void InstanceRegistration_StillWorks()
    {
        var customEncryptor = new FakeEncryptor();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IConfigEncryptor>(customEncryptor);

        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() fails because the fake SQL Server is unreachable.
        }

        // The activator should NOT be registered for instance registrations.
        builder.Services
            .Any(x => x.ServiceType == typeof(IHostedService) &&
                      x.ImplementationType == typeof(DbConfigEncryptorActivator))
            .ShouldBeFalse("DbConfigEncryptorActivator should not be registered for instance-registered encryptors");

        // The DI-resolved encryptor must be the pre-registered instance.
        var sp = builder.Services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IConfigEncryptor>();
        resolved.ShouldBeSameAs(customEncryptor);
    }

    /// <summary>
    /// Verifies that a type-mapped <c>IConfigEncryptor</c> registration does NOT throw during
    /// <c>AddDbConfig</c> (v1.4 behavior). The activator hosted service is registered.
    /// </summary>
    [TimedFact]
    public void TypeMappedRegistration_AddDbConfig_DoesNotThrow_AndRegistersActivator()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IConfigEncryptor, FakeEncryptor>();

        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("DbConfig failed to load", StringComparison.Ordinal))
        {
            // Expected: Load() fails because the fake SQL Server is unreachable.
        }

        // The DbConfigEncryptorActivator must be registered as a hosted service.
        builder.Services
            .Any(x => x.ServiceType == typeof(IHostedService) &&
                      x.ImplementationType == typeof(DbConfigEncryptorActivator))
            .ShouldBeTrue("DbConfigEncryptorActivator should be registered for type-mapped encryptors");
    }

    /// <summary>
    /// Verifies that an encryptor whose constructor takes a DI-injected dependency
    /// (<see cref="ILogger{T}"/>) is resolved correctly when using the type-mapped path.
    /// </summary>
    [TimedFact]
    public void TypeMappedRegistration_WithDependencies_ResolvesCorrectly()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Register logging so the encryptor's logger dependency can be satisfied.
        builder.Services.AddLogging();

        // Register a custom encryptor that requires ILogger<T> via DI.
        builder.Services.AddSingleton<IConfigEncryptor, TestKmsEncryptor>();

        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("DbConfig failed to load", StringComparison.Ordinal))
        {
            // Expected: Load() fails because the fake SQL Server is unreachable.
        }

        // Build the DI container and resolve the encryptor — should succeed because ILogger is available.
        var sp = builder.Services.BuildServiceProvider();

        // Must resolve without throwing.
        var encryptor = sp.GetRequiredService<IConfigEncryptor>();
        encryptor.ShouldBeOfType<TestKmsEncryptor>();

        // The encryptor must be operational.
        var ciphertext = encryptor.Protect("test-value");
        var plaintext = encryptor.Unprotect(ciphertext);
        plaintext.ShouldBe("test-value");
    }

    /// <summary>
    /// Regression test for the instance-registered encryption layering bug
    /// (see tasks/encryption-layering-audit.md). With an instance-registered encryptor,
    /// the polling-side store gets passthrough encryption, the provider receives raw
    /// ciphertext into _tenantData, and SetEncryptor is invoked synchronously by
    /// AddDbConfig so secret reads succeed immediately after host construction.
    /// This test mirrors that wiring shape directly (without spinning up a real DB).
    /// </summary>
    [TimedFact]
    public void InstanceRegisteredEncryptor_ProviderDecryptsSecretOnRead()
    {
        var encryptor = new FakeEncryptor();
        var ciphertext = encryptor.Protect("plaintext-via-instance-path");

        ConfigEntryRecord[] entries =
        [
            new ConfigEntryRecord(App, Env, string.Empty, "Stripe:Key", ciphertext, true, DateTimeOffset.UtcNow, null),
        ];

        // Polling-side store carries raw ciphertext — mirrors the post-fix AddDbConfig
        // wiring where the polling EfCoreConfigStore is given encryptor: null.
        var store = new RawValueStore(entries);
        var options = new DbConfigOptions { Scope = App, Environment = Env };
        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        provider.Load();

        // AddDbConfig invokes SetEncryptor synchronously after Configuration.Add(source).
        // For instance-registered encryptors this is the path that closes the bug.
        provider.SetEncryptor(encryptor);

        // First read of a secret key MUST succeed without throwing — the v1.4 regression
        // would throw "Cannot read secret config value 'Stripe:Key' before host.Build()".
        provider.TryGet("Stripe:Key", out var value).ShouldBeTrue();
        value.ShouldBe("plaintext-via-instance-path");
    }

    /// <summary>
    /// Calling <see cref="DbConfigConfigurationProvider.SetEncryptor"/> twice with the
    /// SAME instance is idempotent and must not throw.
    /// </summary>
    [TimedFact]
    public void SetEncryptor_CalledTwiceWithSameInstance_IsIdempotent()
    {
        var encryptor = new FakeEncryptor();

        ConfigEntryRecord[] entries =
        [
            new ConfigEntryRecord(App, Env, string.Empty, "Key", "value", false, DateTimeOffset.UtcNow, null),
        ];

        var store = new RawValueStore(entries);
        var options = new DbConfigOptions { Scope = App, Environment = Env };
        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        provider.Load();

        provider.SetEncryptor(encryptor);

        // Same instance — must not throw.
        Should.NotThrow(() => provider.SetEncryptor(encryptor));
    }

    /// <summary>
    /// Calling <see cref="DbConfigConfigurationProvider.SetEncryptor"/> twice with
    /// DIFFERENT instances must throw <see cref="InvalidOperationException"/>.
    /// </summary>
    [TimedFact]
    public void SetEncryptor_CalledTwiceWithDifferentInstances_Throws()
    {
        var encryptor1 = new FakeEncryptor();
        var encryptor2 = new FakeEncryptor();

        ConfigEntryRecord[] entries =
        [
            new ConfigEntryRecord(App, Env, string.Empty, "Key", "value", false, DateTimeOffset.UtcNow, null),
        ];

        var store = new RawValueStore(entries);
        var options = new DbConfigOptions { Scope = App, Environment = Env };
        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);
        provider.Load();

        provider.SetEncryptor(encryptor1);

        // Different instance — must throw.
        var ex = Should.Throw<InvalidOperationException>(() => provider.SetEncryptor(encryptor2));
        ex.Message.ShouldContain("already has an encryptor set");
    }

    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// A minimal <see cref="IConfigEncryptor"/> that uses a simple reversible transform.
    /// Protect: prepends "ENC:". Unprotect: removes "ENC:" prefix.
    /// </summary>
    private sealed class FakeEncryptor : IConfigEncryptor
    {
        public string Protect(string plaintext) => "ENC:" + plaintext;

        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith("ENC:", StringComparison.Ordinal)
                ? ciphertext["ENC:".Length..]
                : ciphertext;
    }

    /// <summary>
    /// A custom encryptor that requires <see cref="ILogger{T}"/> via DI.
    /// Used to verify that type-mapped encryptors with constructor dependencies are resolved.
    /// </summary>
    private sealed class TestKmsEncryptor : IConfigEncryptor
    {
        // ReSharper disable once UnusedParameter.Local
        public TestKmsEncryptor(ILogger<TestKmsEncryptor> logger)
        {
            // Logger injected — verifies DI resolution of dependencies works.
        }

        public string Protect(string plaintext) => "KMS:" + plaintext;

        public string Unprotect(string ciphertext) =>
            ciphertext.StartsWith("KMS:", StringComparison.Ordinal)
                ? ciphertext["KMS:".Length..]
                : ciphertext;
    }

    /// <summary>
    /// A minimal <see cref="IConfigStore"/> that returns a fixed set of entries verbatim
    /// (no encryption/decryption). Used to simulate the polling-side store that holds
    /// raw ciphertext for secret entries.
    /// </summary>
    private sealed class RawValueStore : IConfigPollingStore
    {
        private readonly IReadOnlyList<ConfigEntryRecord> _entries;

        public RawValueStore(IReadOnlyList<ConfigEntryRecord> entries)
        {
            _entries = entries;
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct)
        {
            var latest = _entries
                .Where(e =>
                    string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Max(e => (DateTimeOffset?)e.ModifiedUtc);
            return Task.FromResult(latest);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
        {
            var latest = _entries
                .Where(e =>
                    scopes.Contains(e.Scope, StringComparer.OrdinalIgnoreCase) &&
                    string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Max(e => (DateTimeOffset?)e.ModifiedUtc);
            return Task.FromResult(latest);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
            string scope, string environment, string tenantId, CancellationToken ct)
        {
            var latest = _entries
                .Where(e =>
                    string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.TenantId, tenantId, StringComparison.Ordinal))
                .Max(e => (DateTimeOffset?)e.ModifiedUtc);
            return Task.FromResult(latest);
        }

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(
            string scope, string environment, CancellationToken ct)
        {
            var result = _entries
                .Where(e =>
                    string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
            string scope, string environment, CancellationToken ct)
            => GetLatestModifiedUtcAsync(scope, environment, ct);

        public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
        {
            var result = scopes
                .SelectMany(scope => _entries
                    .Where(e =>
                        string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }

        public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
            IReadOnlyList<string> scopes, string environment, CancellationToken ct)
            => GetLatestModifiedUtcScopedAsync(scopes, environment, ct);
    }
}
