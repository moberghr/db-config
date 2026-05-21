using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Verifies that <see cref="InMemoryConfigStore"/> + <see cref="PassthroughConfigEncryptor"/>
/// round-trips IsSecret entries correctly. These tests confirm the default fallback path
/// (no real Data Protection setup) works end-to-end without value corruption.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryStoreEncryptionRoundTripTests
{
    private const string App = "RoundTripApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task Upsert_IsSecretTrue_WithPassthroughEncryptor_RoundTripsPlaintext()
    {
        // PassthroughConfigEncryptor is the default when null is passed to InMemoryConfigStore.
        var store = new InMemoryConfigStore(encryptor: null);
        const string plaintext = "passthrough-secret-value";

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "Secret:Key", plaintext, true, DateTimeOffset.UtcNow, "tester"),
            CancellationToken.None);

        var retrieved = await store.GetAsync(App, Env, "Secret:Key", CancellationToken.None);

        retrieved.ShouldNotBeNull();
        retrieved!.IsSecret.ShouldBeTrue();

        // Passthrough encryptor returns the value verbatim — round-trip must preserve the string.
        retrieved.Value.ShouldBe(plaintext);
    }

    [TimedFact]
    public async Task GetHistory_WithPassthroughEncryptor_DecryptsCorrectly()
    {
        var auditStore = new InMemoryConfigAuditStore();
        var store = new InMemoryConfigStore(encryptor: null, auditStore, enableAuditLog: true);
        const string plaintextV1 = "secret-v1";
        const string plaintextV2 = "secret-v2";
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "History:Key", plaintextV1, true, t, "tester"),
            CancellationToken.None);

        await store.UpsertAsync(
            new ConfigEntryRecord(App, Env, string.Empty, "History:Key", plaintextV2, true, t.AddSeconds(1), "tester"),
            CancellationToken.None);

        var history = await auditStore.GetHistoryAsync(App, Env, "History:Key", 10, CancellationToken.None);

        history.Count.ShouldBe(2);

        // Most-recent-first order.
        history[0].Action.ShouldBe(ConfigAuditAction.Update);
        history[0].IsSecret.ShouldBeTrue();

        // Passthrough encryptor stores and retrieves verbatim — values must be readable.
        history[0].NewValue.ShouldBe(plaintextV2);
        history[0].OldValue.ShouldBe(plaintextV1);

        history[1].Action.ShouldBe(ConfigAuditAction.Insert);
        history[1].NewValue.ShouldBe(plaintextV1);
        history[1].OldValue.ShouldBeNull();
    }
}
