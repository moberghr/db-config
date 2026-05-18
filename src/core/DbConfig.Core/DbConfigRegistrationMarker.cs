namespace DbConfig.Core;

/// <summary>
/// Sentinel singleton registered by the <c>AddDbConfig</c> extension on
/// <c>IHostApplicationBuilder</c> to bridge the DI and configuration system.
/// Holds a reference back to the <see cref="DbConfigBuilder"/> so that the host DI can
/// resolve <see cref="IDbConfigReloadSignal"/> by reading the provider created during
/// host construction.
/// </summary>
internal sealed class DbConfigRegistrationMarker
{
    internal DbConfigRegistrationMarker(DbConfigBuilder builder)
    {
        Builder = builder;
    }

    /// <summary>The builder configured during AddDbConfig.</summary>
    internal DbConfigBuilder Builder { get; }

    private DbConfigConfigurationSource? _source;

    /// <summary>
    /// The configuration source added during AddDbConfig. Null until
    /// <see cref="SetSource"/> is called.
    /// </summary>
    internal DbConfigConfigurationSource? Source => _source;

    /// <summary>
    /// Sets <see cref="Source"/> exactly once. Throws if called a second time.
    /// </summary>
    internal void SetSource(DbConfigConfigurationSource source)
    {
        if (_source is not null)
        {
            throw new InvalidOperationException(
                "DbConfigRegistrationMarker.Source has already been set. " +
                "AddDbConfig may only be invoked once per host.");
        }

        _source = source;
    }
}
