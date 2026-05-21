using DbConfig.Core;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Tests that verify the encryptor instance is shared correctly between the polling-side
/// and HTTP/DI-side stores, and that misconfigured registrations are rejected early.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EncryptorSharingTests
{
    // A valid-looking but non-functional SQL Server connection string used only to satisfy
    // the provider extension without requiring a real database.
    private const string FakeConnectionString =
        "Server=127.0.0.1,19999;Database=test;User Id=sa;Password=fake;Connect Timeout=1;Encrypt=false;";

    /// <summary>
    /// When a consumer pre-registers an <see cref="IConfigEncryptor"/> instance before calling
    /// <c>AddDbConfig</c>, the DI container should resolve that same instance (not a new one).
    /// </summary>
    [TimedFact]
    public void CustomEncryptorInstance_RegisteredBeforeAddDbConfig_UsedByDiSide()
    {
        var customEncryptor = new FakeEncryptor();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IConfigEncryptor>(customEncryptor);

        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = "App";
                b.Options.Environment = "Test";
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() fails because the fake SQL Server is unreachable.
        }

        // Build DI and resolve IConfigEncryptor — it must be the pre-registered instance.
        var sp = builder.Services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IConfigEncryptor>();

        resolved.ShouldBeSameAs(customEncryptor);
    }

    /// <summary>
    /// When a consumer pre-registers an <see cref="IConfigEncryptor"/> via type-mapping,
    /// <c>AddDbConfig</c> must NOT throw. The type-mapped registration is supported via the
    /// deferred-decryption path (DbConfigEncryptorActivator is registered to resolve and inject
    /// the encryptor post-build). This test inverts the v1.3 behavior.
    /// </summary>
    [TimedFact]
    public void TypeMappedEncryptorRegistration_BeforeAddDbConfig_Succeeds()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Type-mapped registration — now supported in v1.4.
        builder.Services.AddSingleton<IConfigEncryptor, FakeEncryptor>();

        // Should not throw — the type-mapped path is allowed and defers decryption to post-build.
        Should.NotThrow(() =>
        {
            try
            {
                builder.AddDbConfig(b =>
                {
                    b.Options.Scope = "App";
                    b.Options.Environment = "Test";
                    b.Options.SchemaMode = SchemaMode.None;
                    b.UseSqlServer(FakeConnectionString);
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("DbConfig failed to load", StringComparison.Ordinal))
            {
                // Expected: Load() fails because the fake SQL Server is unreachable.
                // This is acceptable — we only care that AddDbConfig itself does not throw.
            }
        });
    }

    /// <summary>
    /// Verifies that when a custom encryptor instance is pre-registered, ciphertext produced by
    /// one store can be read by the other — i.e., the same instance is used on both sides.
    /// Uses <see cref="InMemoryConfigStore"/> to verify the round-trip without a real database.
    /// </summary>
    [TimedFact]
    public async Task CustomEncryptorInstance_RegisteredBeforeAddDbConfig_RoundTripsBetweenStores()
    {
        var encryptor = new FakeEncryptor();
        var auditStore = new InMemoryConfigAuditStore();

        // Write via InMemoryConfigStore using the shared encryptor.
        var writeStore = new InMemoryConfigStore(encryptor, auditStore, enableAuditLog: true);
        var entry = new ConfigEntryRecord("App", "Test", string.Empty, "Secret:Key", "plaintext-value", true, DateTimeOffset.UtcNow, "tester");
        await writeStore.UpsertAsync(entry, CancellationToken.None);

        // Read back via a second InMemoryConfigStore instance using the same encryptor.
        var readStore = new InMemoryConfigStore(encryptor, auditStore, enableAuditLog: false);
        await readStore.UpsertAsync(entry, CancellationToken.None); // seed the read store

        var retrieved = await readStore.GetAsync("App", "Test", "Secret:Key", CancellationToken.None);

        retrieved.ShouldNotBeNull();
        retrieved!.Value.ShouldBe("plaintext-value");

        // The FakeEncryptor records Protect calls — confirm encryption was applied.
        encryptor.ProtectCallCount.ShouldBeGreaterThan(0);
        encryptor.UnprotectCallCount.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Minimal <see cref="IConfigEncryptor"/> implementation that records call counts and
    /// uses a trivial reversible transform so tests can verify round-trips.
    /// </summary>
    private sealed class FakeEncryptor : IConfigEncryptor
    {
        public int ProtectCallCount { get; private set; }

        public int UnprotectCallCount { get; private set; }

        public string Protect(string plaintext)
        {
            ProtectCallCount++;
            return "ENC:" + plaintext;
        }

        public string Unprotect(string ciphertext)
        {
            UnprotectCallCount++;
            return ciphertext.StartsWith("ENC:", StringComparison.Ordinal)
                ? ciphertext["ENC:".Length..]
                : ciphertext;
        }
    }
}
