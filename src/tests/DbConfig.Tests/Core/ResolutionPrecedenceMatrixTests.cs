using DbConfig.Core;
using DbConfig.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DbConfig.Tests.Core;

/// <summary>
/// Full 2^5 = 32-case precedence matrix proving that
/// <c>(tenant=Acme, AppName=MyApp) → (tenant=Acme, AppName=Shared)
/// → (global, AppName=MyApp) → (global, AppName=Shared) → null</c>
/// is the resolution order for a host with <c>AppName="MyApp"</c> and
/// <c>IncludeScopes=["Shared"]</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResolutionPrecedenceMatrixTests
{
    private const string OwnApp = "MyApp";
    private const string SharedApp = "Shared";
    private const string Env = "Prod";
    private const string AcmeTenant = "Acme";
    private const string TheKey = "Stripe:Key";

    public sealed record MatrixCase(
        bool HasAcmeApp,
        bool HasAcmeShared,
        bool HasGlobalApp,
        bool HasGlobalShared,
        string? ResolverTenant,
        string? Expected);

    public static IEnumerable<TheoryDataRow<MatrixCase>> Cases()
    {
        // 2^5 = 32 combinations across (acmeApp, acmeShared, globalApp, globalShared, tenant ∈ {null, "Acme"}).
        foreach (var hasAcmeApp in new[] { false, true })
        {
            foreach (var hasAcmeShared in new[] { false, true })
            {
                foreach (var hasGlobalApp in new[] { false, true })
                {
                    foreach (var hasGlobalShared in new[] { false, true })
                    {
                        foreach (var tenant in new[] { null, AcmeTenant })
                        {
                            var expected = ComputeExpected(hasAcmeApp, hasAcmeShared, hasGlobalApp, hasGlobalShared, tenant);
                            yield return new TheoryDataRow<MatrixCase>(
                                new MatrixCase(hasAcmeApp, hasAcmeShared, hasGlobalApp, hasGlobalShared, tenant, expected));
                        }
                    }
                }
            }
        }
    }

    private static string? ComputeExpected(
        bool hasAcmeApp,
        bool hasAcmeShared,
        bool hasGlobalApp,
        bool hasGlobalShared,
        string? tenant)
    {
        if (string.Equals(tenant, AcmeTenant, StringComparison.Ordinal))
        {
            if (hasAcmeApp)
            {
                return "acme-app";
            }

            if (hasAcmeShared)
            {
                return "acme-shared";
            }
        }

        if (hasGlobalApp)
        {
            return "global-app";
        }

        if (hasGlobalShared)
        {
            return "global-shared";
        }

        return null;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Resolves_AccordingToPrecedence(MatrixCase data)
    {
        var store = new InMemoryConfigStore();
        var t = DateTimeOffset.UtcNow;
        var ct = TestContext.Current.CancellationToken;

        if (data.HasAcmeApp)
        {
            await store.UpsertAsync(new ConfigEntry(OwnApp, Env, AcmeTenant, TheKey, "acme-app", false, t, null), ct);
        }

        if (data.HasAcmeShared)
        {
            await store.UpsertAsync(new ConfigEntry(SharedApp, Env, AcmeTenant, TheKey, "acme-shared", false, t, null), ct);
        }

        if (data.HasGlobalApp)
        {
            await store.UpsertAsync(new ConfigEntry(OwnApp, Env, string.Empty, TheKey, "global-app", false, t, null), ct);
        }

        if (data.HasGlobalShared)
        {
            await store.UpsertAsync(new ConfigEntry(SharedApp, Env, string.Empty, TheKey, "global-shared", false, t, null), ct);
        }

        var options = new DbConfigOptions
        {
            AppName = OwnApp,
            Environment = Env,
            ReloadInterval = TimeSpan.FromSeconds(30),
            IncludeScopes = [SharedApp],
        };

        var provider = new DbConfigConfigurationProvider(
            options,
            store,
            TimeProvider.System,
            NullLoggerFactory.Instance);

        provider.Load();

        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new MutableTenantResolver(data.ResolverTenant));
        await using var sp = services.BuildServiceProvider();
        provider.HostServiceProvider = sp;

        var found = provider.TryGet(TheKey, out var value);

        if (data.Expected is null)
        {
            found.ShouldBeFalse();
            value.ShouldBeNull();
        }
        else
        {
            found.ShouldBeTrue();
            value.ShouldBe(data.Expected);
        }
    }
}
