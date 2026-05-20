using DbConfig.Core;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class InMemoryStoreGetAsyncTests
{
    private const string App = "TestApp";
    private const string Env = "Test";

    [TimedFact]
    public async Task GetAsync_KeyExists_ReturnsEntry()
    {
        var store = new InMemoryConfigStore();
        var entry = new ConfigEntry(App, Env, string.Empty, "Section:Key", "hello", false, DateTimeOffset.UtcNow, "user1");
        await store.UpsertAsync(entry, CancellationToken.None);

        var result = await store.GetAsync(App, Env, "Section:Key", CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Scope.ShouldBe(App);
        result.Environment.ShouldBe(Env);
        result.Key.ShouldBe("Section:Key");
        result.Value.ShouldBe("hello");
        result.IsSecret.ShouldBeFalse();
        result.ModifiedBy.ShouldBe("user1");
    }

    [TimedFact]
    public async Task GetAsync_KeyDoesNotExist_ReturnsNull()
    {
        var store = new InMemoryConfigStore();
        await store.UpsertAsync(new ConfigEntry(App, Env, string.Empty, "OtherKey", "v", false, DateTimeOffset.UtcNow, null), CancellationToken.None);

        var result = await store.GetAsync(App, Env, "NonExistentKey", CancellationToken.None);

        result.ShouldBeNull();
    }

    [TimedFact]
    public async Task GetAsync_DifferentScope_ReturnsNull()
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;

        // Same key, different app — should not match.
        await store.UpsertAsync(new ConfigEntry("OtherApp", Env, string.Empty, "SharedKey", "v1", false, t, null), CancellationToken.None);

        // Same key, different env — should not match.
        await store.UpsertAsync(new ConfigEntry(App, "OtherEnv", string.Empty, "SharedKey", "v2", false, t, null), CancellationToken.None);

        var result = await store.GetAsync(App, Env, "SharedKey", CancellationToken.None);

        result.ShouldBeNull();
    }
}
