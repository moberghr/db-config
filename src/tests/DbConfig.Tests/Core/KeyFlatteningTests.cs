using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class KeyFlatteningTests
{
    private static DbConfigConfigurationProvider CreateProvider(IConfigStore store)
    {
        var options = new DbConfigOptions
        {
            Scope = "TestApp",
            Environment = "Test",
            ReloadInterval = TimeSpan.FromMinutes(5),
        };

        return new DbConfigConfigurationProvider(options, store, TimeProvider.System, NullLoggerFactory.Instance);
    }

    [TimedFact]
    public void Load_HierarchicalKey_IsReachableViaColonNotation()
    {
        var store = new InMemoryConfigStore();
        var entry = new ConfigEntry(
            Scope: "TestApp",
            Environment: "Test",
            TenantId: string.Empty,
            Key: "Section:Sub",
            Value: "hello",
            IsSecret: false,
            ModifiedUtc: DateTimeOffset.UtcNow,
            ModifiedBy: null);
        store.UpsertAsync(entry, CancellationToken.None).GetAwaiter().GetResult();

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("Section:Sub", out var value).ShouldBeTrue();
        value.ShouldBe("hello");
    }

    [TimedFact]
    public void Load_HierarchicalKey_IsReachableViaGetSection()
    {
        var store = new InMemoryConfigStore();
        var entry = new ConfigEntry(
            Scope: "TestApp",
            Environment: "Test",
            TenantId: string.Empty,
            Key: "Section:Sub",
            Value: "hello",
            IsSecret: false,
            ModifiedUtc: DateTimeOffset.UtcNow,
            ModifiedBy: null);
        store.UpsertAsync(entry, CancellationToken.None).GetAwaiter().GetResult();

        var provider = CreateProvider(store);
        provider.Load();

        var config = new ConfigurationBuilder()
            .Add(new ProviderSource(provider))
            .Build();

        config.GetSection("Section")["Sub"].ShouldBe("hello");
    }

    [TimedFact]
    public void Load_TopLevelKey_IsReachableDirectly()
    {
        var store = new InMemoryConfigStore();
        var entry = new ConfigEntry(
            Scope: "TestApp",
            Environment: "Test",
            TenantId: string.Empty,
            Key: "SimpleKey",
            Value: "value",
            IsSecret: false,
            ModifiedUtc: DateTimeOffset.UtcNow,
            ModifiedBy: null);
        store.UpsertAsync(entry, CancellationToken.None).GetAwaiter().GetResult();

        var provider = CreateProvider(store);
        provider.Load();

        var config = new ConfigurationBuilder()
            .Add(new ProviderSource(provider))
            .Build();

        config["SimpleKey"].ShouldBe("value");
    }

    [TimedFact]
    public void Load_MultipleHierarchicalKeys_AllReachable()
    {
        var store = new InMemoryConfigStore();
        var now = DateTimeOffset.UtcNow;

        store.UpsertAsync(new ConfigEntry("TestApp", "Test", string.Empty, "A:B", "ab", false, now, null), CancellationToken.None).GetAwaiter().GetResult();
        store.UpsertAsync(new ConfigEntry("TestApp", "Test", string.Empty, "A:C", "ac", false, now, null), CancellationToken.None).GetAwaiter().GetResult();
        store.UpsertAsync(new ConfigEntry("TestApp", "Test", string.Empty, "X", "x", false, now, null), CancellationToken.None).GetAwaiter().GetResult();

        var provider = CreateProvider(store);
        provider.Load();

        var config = new ConfigurationBuilder()
            .Add(new ProviderSource(provider))
            .Build();

        config.GetSection("A")["B"].ShouldBe("ab");
        config.GetSection("A")["C"].ShouldBe("ac");
        config["X"].ShouldBe("x");
    }

    [TimedFact]
    public void Load_NullValue_IsStoredAsNull()
    {
        var store = new InMemoryConfigStore();
        var entry = new ConfigEntry(
            Scope: "TestApp",
            Environment: "Test",
            TenantId: string.Empty,
            Key: "NullKey",
            Value: null,
            IsSecret: false,
            ModifiedUtc: DateTimeOffset.UtcNow,
            ModifiedBy: null);
        store.UpsertAsync(entry, CancellationToken.None).GetAwaiter().GetResult();

        var provider = CreateProvider(store);
        provider.Load();

        provider.TryGet("NullKey", out var value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    /// <summary>
    /// Minimal IConfigurationSource wrapper so we can pass a provider instance directly
    /// to ConfigurationBuilder.Add without going through the full AddDbConfig pipeline.
    /// </summary>
    private sealed class ProviderSource : IConfigurationSource
    {
        private readonly IConfigurationProvider _provider;

        public ProviderSource(IConfigurationProvider provider) => _provider = provider;

        public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider;
    }
}
