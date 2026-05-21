using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class InMemoryStoreScopedTests
{
    private const string Env = "Test";

    [TimedFact]
    public async Task GetAllScopedAsync_ReturnsRowsFromAllListedScopes()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord("Shared", Env, string.Empty, "Key1", "shared-v", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord("MyApp", Env, string.Empty, "Key2", "own-v", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord("Other", Env, string.Empty, "Key3", "other-v", false, t, null), CancellationToken.None);

        var result = await store.GetAllScopedAsync(["Shared", "MyApp"], Env, CancellationToken.None);

        result.Count.ShouldBe(2);
        result.ShouldContain(e => e.Scope == "Shared" && e.Key == "Key1");
        result.ShouldContain(e => e.Scope == "MyApp" && e.Key == "Key2");
    }

    [TimedFact]
    public async Task GetAllScopedAsync_ReturnsEntriesInInputScopeOrder()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord("MyApp", Env, string.Empty, "Key", "own", false, t, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord("Shared", Env, string.Empty, "Key", "shared", false, t, null), CancellationToken.None);

        // Input order: Shared first, MyApp second — caller relies on this for precedence.
        var result = await store.GetAllScopedAsync(["Shared", "MyApp"], Env, CancellationToken.None);

        result.Count.ShouldBe(2);
        result[0].Scope.ShouldBe("Shared");
        result[1].Scope.ShouldBe("MyApp");
    }

    [TimedFact]
    public async Task GetAllScopedAsync_EmptyScopeList_ReturnsEmpty()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        await store.UpsertAsync(new ConfigEntryRecord("MyApp", Env, string.Empty, "Key", "v", false, t, null), CancellationToken.None);

        var result = await store.GetAllScopedAsync([], Env, CancellationToken.None);

        result.Count.ShouldBe(0);
    }

    [TimedFact]
    public async Task GetLatestModifiedUtcScopedAsync_ReturnsMaxAcrossScopes()
    {
        var store = new InMemoryConfigStore();
        var t0 = DateTimeOffset.UtcNow;
        var t1 = t0.AddSeconds(10);

        await store.UpsertAsync(new ConfigEntryRecord("Shared", Env, string.Empty, "Key1", "v1", false, t0, null), CancellationToken.None);
        await store.UpsertAsync(new ConfigEntryRecord("MyApp", Env, string.Empty, "Key2", "v2", false, t1, null), CancellationToken.None);

        var result = await store.GetLatestModifiedUtcScopedAsync(["Shared", "MyApp"], Env, CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Value.ShouldBe(t1);
    }

    [TimedFact]
    public async Task GetLatestModifiedUtcScopedAsync_NoEntriesInAnyScope_ReturnsNull()
    {
        var store = new InMemoryConfigStore();

        var result = await store.GetLatestModifiedUtcScopedAsync(["Shared", "MyApp"], Env, CancellationToken.None);

        result.ShouldBeNull();
    }
}
