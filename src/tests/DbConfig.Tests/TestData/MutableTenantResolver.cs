using DbConfig.Core;

namespace DbConfig.Tests.TestData;

/// <summary>
/// Shared test <see cref="ITenantResolver"/> whose <see cref="Tenant"/> can be reassigned
/// between assertions. Replaces the per-file <c>FakeResolver</c> duplicates that previously
/// existed across multi-tenant unit tests.
/// </summary>
public sealed class MutableTenantResolver : ITenantResolver
{
    public MutableTenantResolver()
    {
    }

    public MutableTenantResolver(string? tenant) => Tenant = tenant;

    public string? Tenant { get; set; }

    public string? Resolve() => Tenant;
}
