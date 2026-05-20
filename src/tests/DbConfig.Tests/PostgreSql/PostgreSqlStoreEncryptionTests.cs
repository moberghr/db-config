using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.PostgreSql;
using DbConfig.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DbConfig.Tests.PostgreSql;

/// <summary>
/// Verifies that <see cref="EfCoreConfigStore"/> encrypts secret values at rest and
/// transparently decrypts them on read.
/// </summary>
[Trait("Category", "PostgreSql")]
[Collection(PostgreSqlFixture.CollectionName)]
public sealed class PostgreSqlStoreEncryptionTests : IAsyncLifetime
{
    private const string App = "TestApp";
    private const string Env = "Production";

    private readonly PostgreSqlFixture _fixture;
    private EfCoreConfigStore _store = null!;

    public PostgreSqlStoreEncryptionTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
        _store = new EfCoreConfigStore(
            _fixture.DbContextFactory,
            new PostgreSqlUniqueConstraintDetector(),
            TimeProvider.System,
            _fixture.Encryptor);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact(30_000)]
    public async Task Upsert_SecretEntry_ValueStoredAsCiphertext()
    {
        const string plaintext = "my-secret-password";
        var entry = new ConfigEntry(App, Env, string.Empty, "SecretKey", plaintext, true, DateTimeOffset.UtcNow, null);

        await _store.UpsertAsync(entry, CancellationToken.None);

        // Read the raw DB row directly via the DbContext to assert at-rest ciphertext.
        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var raw = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == App && x.Environment == Env && x.Key == "SecretKey")
            .FirstOrDefaultAsync(CancellationToken.None);

        raw.ShouldNotBeNull();
        raw!.Value.ShouldNotBeNull();
        raw.Value.ShouldNotBe(plaintext);
    }

    [TimedFact(30_000)]
    public async Task Upsert_NonSecretEntry_ValueStoredAsPlaintext()
    {
        const string value = "plain-value";
        var entry = new ConfigEntry(App, Env, string.Empty, "PlainKey", value, false, DateTimeOffset.UtcNow, null);

        await _store.UpsertAsync(entry, CancellationToken.None);

        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var raw = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == App && x.Environment == Env && x.Key == "PlainKey")
            .FirstOrDefaultAsync(CancellationToken.None);

        raw.ShouldNotBeNull();
        raw!.Value.ShouldBe(value);
    }

    [TimedFact(30_000)]
    public async Task GetAsync_SecretEntry_ReturnsDecryptedPlaintext()
    {
        const string plaintext = "transparent-decryption-value";
        var entry = new ConfigEntry(App, Env, string.Empty, "SecretTransparent", plaintext, true, DateTimeOffset.UtcNow, null);

        await _store.UpsertAsync(entry, CancellationToken.None);

        var result = await _store.GetAsync(App, Env, "SecretTransparent", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe(plaintext);
    }

    [TimedFact(30_000)]
    public async Task GetAllAsync_SecretEntries_AllDecrypted()
    {
        var t = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "S1", "secret-one", true, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "S2", "secret-two", true, t, null), CancellationToken.None);
        await _store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "P1", "plain-one", false, t, null), CancellationToken.None);

        var results = await _store.GetAllAsync(App, Env, CancellationToken.None);

        results.Count.ShouldBe(3);

        var s1 = results.First(x => string.Equals(x.Key, "S1", StringComparison.Ordinal));
        var s2 = results.First(x => string.Equals(x.Key, "S2", StringComparison.Ordinal));
        var p1 = results.First(x => string.Equals(x.Key, "P1", StringComparison.Ordinal));

        s1.Value.ShouldBe("secret-one");
        s2.Value.ShouldBe("secret-two");
        p1.Value.ShouldBe("plain-one");
    }

    [TimedFact(30_000)]
    public async Task IsSecretFlippedFalseAfterEncryption_GetAsyncReturnsCiphertextAsValue()
    {
        // Insert as secret — value is encrypted at rest.
        const string plaintext = "originally-secret";
        await _store.UpsertAsync(
            new ConfigEntry(App, Env, string.Empty, "FlippedKey", plaintext, true, DateTimeOffset.UtcNow, null),
            CancellationToken.None);

        // Manually flip the IsSecret column in the DB while keeping the encrypted value.
        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var entity = await context.ConfigEntries
            .Where(x => x.Scope == App && x.Environment == Env && x.Key == "FlippedKey")
            .FirstOrDefaultAsync(CancellationToken.None);

        entity.ShouldNotBeNull();
        var encryptedValue = entity!.Value;

        // Flip the IsSecret bit without touching the value (still ciphertext).
        entity.IsSecret = false;
        await context.SaveChangesAsync(CancellationToken.None);

        // The store will NOT attempt to decrypt non-secret entries.
        // Therefore the raw ciphertext is returned as-is.
        var result = await _store.GetAsync(App, Env, "FlippedKey", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.IsSecret.ShouldBeFalse();
        result.Value.ShouldBe(encryptedValue);
        result.Value.ShouldNotBe(plaintext);
    }
}
