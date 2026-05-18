using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbConfig.Core;

/// <summary>
/// Configuration source that creates a <see cref="DbConfigConfigurationProvider"/>.
/// Registered by the <c>AddDbConfig</c> extension on <c>IHostApplicationBuilder</c>.
/// Also exposes the constructed provider so DI can resolve <see cref="IDbConfigReloadSignal"/>
/// from it after the configuration system is built.
/// </summary>
internal sealed class DbConfigConfigurationSource : IConfigurationSource
{
    private readonly DbConfigOptions _options;
    private readonly IConfigStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbConfigConfigurationSource"/> class with
    /// directly supplied dependencies.
    /// </summary>
    internal DbConfigConfigurationSource(
        DbConfigOptions options,
        IConfigStore store,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _store = store;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// The provider instance created by the last <see cref="Build"/> call.
    /// Set after the configuration system calls <see cref="Build"/>; null before that.
    /// </summary>
    internal DbConfigConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var provider = new DbConfigConfigurationProvider(_options, _store, _timeProvider, _loggerFactory);
        Provider = provider;
        return provider;
    }
}
