using DbConfig.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Hosted service that wires the host <see cref="IServiceProvider"/> into the polling-side
/// <see cref="DbConfigConfigurationProvider"/> after the host has been built, enabling
/// lazy resolution of <see cref="ITenantResolver"/> from host DI.
/// </summary>
/// <remarks>
/// Always registered by <c>AddDbConfig</c>. On <see cref="StartAsync"/>, passes the live
/// service provider to the provider so that <c>TryGet</c> can resolve
/// <see cref="ITenantResolver"/> lazily. Before this runs (during early config reads at
/// startup), the provider falls back to <see cref="NullTenantResolver"/> and returns global
/// entries only — matching the pre-build behavior of the encryptor activator.
/// </remarks>
internal sealed class DbConfigTenantActivator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DbConfigRegistrationMarker _marker;

    public DbConfigTenantActivator(IServiceProvider serviceProvider, DbConfigRegistrationMarker marker)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(marker);

        _serviceProvider = serviceProvider;
        _marker = marker;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var provider = _marker.Source?.Provider;

        if (provider is not null)
        {
            provider.HostServiceProvider = _serviceProvider;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
