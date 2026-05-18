using DbConfig.Core;
using Microsoft.Extensions.Hosting;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Hosted service that wires a type-mapped or factory-based <see cref="IConfigEncryptor"/>
/// into the polling-side <see cref="DbConfigConfigurationProvider"/> after the host has been
/// built and the service provider is ready.
/// </summary>
/// <remarks>
/// <para>
/// When <c>AddDbConfig</c> detects a type-mapped <c>IConfigEncryptor</c> registration
/// (e.g. <c>services.AddSingleton&lt;IConfigEncryptor, MyImpl&gt;()</c>), it registers
/// this hosted service and passes <see langword="null"/> as the encryptor to the polling-side
/// store. The store stores raw values (ciphertext for secret rows) until this service's
/// <see cref="StartAsync"/> runs, at which point the encryptor is resolved from the host
/// service provider and injected into the provider via <c>SetEncryptor</c>.
/// </para>
/// <para>
/// After <c>SetEncryptor</c> is called, <c>TryGet</c> on the provider decrypts secret values
/// on demand. Attempting to read a secret key before this service has started throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
internal sealed class DbConfigEncryptorActivator : IHostedService
{
    private readonly IConfigEncryptor _encryptor;
    private readonly DbConfigRegistrationMarker _marker;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbConfigEncryptorActivator"/> class.
    /// </summary>
    /// <param name="encryptor">The encryptor resolved from the host service provider.</param>
    /// <param name="marker">The registration marker holding a reference to the configuration source.</param>
    public DbConfigEncryptorActivator(IConfigEncryptor encryptor, DbConfigRegistrationMarker marker)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        ArgumentNullException.ThrowIfNull(marker);

        _encryptor = encryptor;
        _marker = marker;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var provider = _marker.Source?.Provider;

        if (provider is not null)
        {
            provider.SetEncryptor(_encryptor);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No-op: the encryptor does not require cleanup.
        return Task.CompletedTask;
    }
}
