using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DbConfig.Core;

/// <summary>
/// Default <see cref="ITenantConfigReader"/> implementation. Activates an
/// <see cref="System.Threading.AsyncLocal{T}"/> tenant override on
/// <see cref="DbConfigConfigurationProvider"/> for the duration of a typed bind,
/// then resolves <see cref="IOptionsSnapshot{TOptions}"/> in a fresh DI scope so the
/// standard binding pipeline picks up the override via the polling provider's
/// <c>TryGet</c>.
/// </summary>
internal sealed class TenantConfigReader : ITenantConfigReader
{
    private readonly DbConfigConfigurationProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantConfigReader(DbConfigConfigurationProvider provider, IServiceScopeFactory scopeFactory)
    {
        _provider = provider;
        _scopeFactory = scopeFactory;
    }

    public T GetForTenant<T>(string tenantId)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        // The override and the DI scope are both bound to this async flow. The scope is
        // disposed before the override is restored, ensuring IOptionsSnapshot<T> binds
        // while the override is still active.
        using var overrideScope = _provider.OverrideTenant(tenantId);
        using var diScope = _scopeFactory.CreateScope();

        var snapshot = diScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<T>>();
        return snapshot.Value;
    }
}
