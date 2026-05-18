using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace DbConfig.Core;

/// <summary>
/// <see cref="IConfigurationProvider"/> that polls an <see cref="IConfigStore"/> on a
/// configurable interval and fires <see cref="IChangeToken"/> when new entries are detected.
/// Also implements <see cref="IDbConfigReloadSignal"/> so the HTTP reload endpoint can
/// schedule an immediate out-of-band reload without waiting for the next poll interval.
/// </summary>
/// <remarks>
/// <c>TryGet</c> is tenant-aware: it resolves <see cref="ITenantResolver"/> from host DI
/// (lazily after host build), calls <c>Resolve()</c>, and returns the tenant-specific entry
/// if one exists, falling back to the global (TenantId = "") entry. This means standard
/// <c>IOptionsSnapshot&lt;T&gt;</c> automatically tracks the current tenant per request.
/// <para>
/// <strong>IOptions&lt;T&gt; caveat:</strong> <c>IOptions&lt;T&gt;</c> is singleton-cached
/// and binds once at app startup when no request scope exists. The resolver returns null at
/// that point, so the global entry is bound forever. Consumers MUST use
/// <c>IOptionsSnapshot&lt;T&gt;</c> (scoped per-request) for tenant-aware types.
/// </para>
/// </remarks>
internal sealed class DbConfigConfigurationProvider : ConfigurationProvider, IDbConfigReloadSignal, IDisposable
{
    private readonly DbConfigOptions _options;
    private readonly IConfigStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DbConfigConfigurationProvider> _logger;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Per-tenant snapshot: tenantId → (key → raw value).
    /// Includes the global tenant (key = ""). Raw values — may be ciphertext for secret entries.
    /// </summary>
    private ConcurrentDictionary<string, Dictionary<string, string?>> _tenantData = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-tenant secret flags: tenantId → (key → isSecret).
    /// Used to decide whether to decrypt a value on read.
    /// </summary>
    private ConcurrentDictionary<string, Dictionary<string, bool>> _isSecretByTenantKey = new(StringComparer.Ordinal);

    /// <summary>
    /// Cached <see cref="ITenantResolver"/> instance resolved from host DI after host build.
    /// Null until the first TryGet call after host build. Volatile for cross-thread visibility.
    /// Accessing this field is done via <see cref="ResolveTenantResolverLazy"/> only.
    /// </summary>
    private volatile ITenantResolver? _resolverCached;

    /// <summary>
    /// Marker registration used to access host DI for lazy resolver resolution.
    /// Set after the host is built when the provider gains access to the service provider.
    /// </summary>
    internal IServiceProvider? HostServiceProvider { private get; set; }

    /// <summary>
    /// The encryptor used to decrypt secret values on read. Null until
    /// <see cref="SetEncryptor"/> is called (e.g. by the DbConfigEncryptorActivator hosted service
    /// after host.Build() completes). When null, reading a secret key throws InvalidOperationException.
    /// </summary>
    private volatile IConfigEncryptor? _encryptor;

    private ITimer? _timer;
    private DateTimeOffset? _lastWatermark;
    private bool _disposed;

    internal DbConfigConfigurationProvider(
        DbConfigOptions options,
        IConfigStore store,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _store = store;
        _timeProvider = timeProvider;
        _logger = loggerFactory.CreateLogger<DbConfigConfigurationProvider>();
    }

    /// <summary>
    /// Sets the encryptor to use for on-demand decryption of secret values.
    /// Called by <c>DbConfigEncryptorActivator</c> in its <c>StartAsync</c> after the host
    /// has been built and the service provider is available.
    /// After setting the encryptor, fires a change token so consumers that read post-build
    /// get plaintext values.
    /// </summary>
    internal void SetEncryptor(IConfigEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(encryptor);

        var existing = _encryptor;
        if (existing is not null && !ReferenceEquals(existing, encryptor))
        {
            throw new InvalidOperationException(
                "DbConfigConfigurationProvider already has an encryptor set. " +
                "SetEncryptor may only be called once (or repeatedly with the same instance). " +
                "If you intended to swap encryptors, restart the host with a fresh registration.");
        }

        _encryptor = encryptor;

        // Fire a reload notification so change-token subscribers re-read values as plaintext.
        OnReload();
    }

    /// <summary>
    /// Overrides the base TryGet to apply tenant-aware lookups and lazy decryption for
    /// secret entries.
    /// </summary>
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    ///   <item>Resolve tenant id from <see cref="ITenantResolver"/> (singleton, resolved lazily
    ///     from host DI after host build). If resolver returns null or empty, skip to global.</item>
    ///   <item>If tenant id is non-empty, check <see cref="_tenantData"/> for a tenant-specific
    ///     entry. If found, return it (decrypted if secret).</item>
    ///   <item>Fall back to global (TenantId = "") entry in the base <c>Data</c> dictionary.
    ///     If found, return it (decrypted if secret).</item>
    ///   <item>Return false if neither exists.</item>
    /// </list>
    /// </remarks>
    public override bool TryGet(string key, out string? value)
    {
        var resolver = ResolveTenantResolverLazy();
        var tenantId = resolver.Resolve();

        // Tenant-specific entry (if resolver returned a non-empty tenant).
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantSnapshot = Volatile.Read(ref _tenantData);
            if (tenantSnapshot.TryGetValue(tenantId, out var bag) && bag.TryGetValue(key, out var rawTenantValue))
            {
                value = DecryptIfSecret(tenantId, key, rawTenantValue);
                return true;
            }
        }

        // Global fallback — base Data dict holds global (TenantId = "") entries.
        if (base.TryGet(key, out var rawGlobalValue))
        {
            value = DecryptIfSecret(string.Empty, key, rawGlobalValue);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Lazily resolves <see cref="ITenantResolver"/> from host DI on first call after host
    /// build. Returns <see cref="NullTenantResolver.Instance"/> if no resolver is registered
    /// or host DI is not yet available (pre-build reads).
    /// </summary>
    private ITenantResolver ResolveTenantResolverLazy()
    {
        var cached = _resolverCached;
        if (cached is not null)
        {
            return cached;
        }

        var sp = HostServiceProvider;
        if (sp is null)
        {
            // Pre-build: no host DI available yet — use null resolver (global-only).
            return NullTenantResolver.Instance;
        }

        // Resolve from host DI; fall back to NullTenantResolver if not registered.
        var resolved = sp.GetService<ITenantResolver>() ?? NullTenantResolver.Instance;

        // Cache the resolved instance (singleton; safe to race — all threads converge on same value).
        _resolverCached = resolved;
        return resolved;
    }

    /// <summary>
    /// Layering note: the polling-side <c>IConfigStore</c> is constructed with a
    /// <c>PassthroughConfigEncryptor</c> (see <c>HostApplicationBuilderExtensions.AddDbConfig</c>),
    /// so <c>rawValue</c> for an <c>IsSecret=true</c> entry is ciphertext as stored in
    /// the database. Decryption happens here, exactly once, using the encryptor injected
    /// via <see cref="SetEncryptor"/>. The HTTP-side store, in contrast, has the real
    /// encryptor and decrypts at the store layer for API responses.
    /// </summary>
    private string? DecryptIfSecret(string tenantId, string key, string? rawValue)
    {
        var secretSnapshot = Volatile.Read(ref _isSecretByTenantKey);
        var isSecret = secretSnapshot.TryGetValue(tenantId, out var tenantSecrets) &&
                       tenantSecrets.TryGetValue(key, out var s) && s;

        if (!isSecret)
        {
            return rawValue;
        }

        if (_encryptor is null)
        {
            throw new InvalidOperationException(
                $"Cannot read secret config value '{key}' before host.Build() has returned. " +
                "Move this read into a request handler, hosted service, or OnStarted callback. " +
                "Non-secret values are unaffected.");
        }

        return rawValue is null ? null : _encryptor.Unprotect(rawValue);
    }

    public override void Load()
    {
        // Synchronous first load — ASP.NET Core calls this during host construction.
        // We run the async fetch synchronously. If the store throws, surface a clear
        // InvalidOperationException so the host can report a helpful startup error.
        try
        {
            LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "DbConfig failed to load configuration on startup. " +
                "Verify that the store is reachable and correctly configured.",
                ex);
        }

        // Start the polling timer after a successful first load.
        _timer = _timeProvider.CreateTimer(
            OnTimerTick,
            state: null,
            dueTime: _options.ReloadInterval,
            period: _options.ReloadInterval);
    }

    public void Trigger()
    {
        if (_disposed)
        {
            return;
        }

        // Schedule an immediate reload off the calling thread, mirroring what the timer callback does.
        _ = Task.Run(() => OnTimerTick(state: null), _cts.Token);
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        // Exceptions MUST NOT propagate out of the timer callback — they crash the process.
        try
        {
            PollForChangesAsync(_cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DbConfig reload failed for app '{AppName}' / env '{Environment}'. Previous values retained; will retry on next tick.", _options.AppName, _options.Environment);
        }
    }

    private async Task PollForChangesAsync(CancellationToken ct)
    {
        // Composed path: BuildScopeList() returns [..IncludeScopes, AppName].
        // When IncludeScopes is empty it returns just [AppName], so a host with no scopes
        // still gets multi-tenant coverage via the scoped+all-tenants watermark.
        var scopeList = BuildScopeList();
        var latestWatermark = await _store.GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
            scopeList,
            _options.Environment,
            ct);

        var watermarkAdvanced = latestWatermark.HasValue
            && (_lastWatermark is null || latestWatermark.Value > _lastWatermark.Value);

        if (!watermarkAdvanced)
        {
            return;
        }

        await LoadAsync(ct);
        OnReload();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        // Composed path: load every (scope × tenant) entry in scope order so the in-memory
        // merge below applies last-writer-wins per (tenant, key) tuple using the iteration order.
        var scopeList = BuildScopeList();
        var entries = await _store.GetAllScopedForAllTenantsAsync(scopeList, _options.Environment, ct);

        // Build per-tenant dictionaries.
        var newTenantData = new ConcurrentDictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        var newIsSecretByTenantKey = new ConcurrentDictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        DateTimeOffset? highWatermark = null;

        foreach (var entry in entries)
        {
            var tid = entry.TenantId;

            if (!newTenantData.TryGetValue(tid, out var tenantValues))
            {
                tenantValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                newTenantData[tid] = tenantValues;
            }

            if (!newIsSecretByTenantKey.TryGetValue(tid, out var tenantSecrets))
            {
                tenantSecrets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                newIsSecretByTenantKey[tid] = tenantSecrets;
            }

            tenantValues[entry.Key] = entry.Value;
            tenantSecrets[entry.Key] = entry.IsSecret;

            if (highWatermark is null || entry.ModifiedUtc > highWatermark.Value)
            {
                highWatermark = entry.ModifiedUtc;
            }
        }

        // Defense in depth: base Data dict exposes ONLY global (TenantId = "") entries.
        // This is what IConfiguration[key] reads — tenant-specific entries are NOT here.
        var newData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (newTenantData.TryGetValue(string.Empty, out var globalValues))
        {
            foreach (var kvp in globalValues)
            {
                newData[kvp.Key] = kvp.Value;
            }
        }

        Data = newData;
        Volatile.Write(ref _tenantData, newTenantData);
        Volatile.Write(ref _isSecretByTenantKey, newIsSecretByTenantKey);
        _lastWatermark = highWatermark;

        // Invalidate the cached resolver so any updated DI registrations take effect on next read.
        // (In practice this is a no-op for singletons — the same instance is re-resolved.)
        _resolverCached = null;
    }

    /// <summary>
    /// Builds the ordered scope list for multi-scope reads: IncludeScopes (lowest precedence first,
    /// deduplicated case-insensitively) followed by AppName (highest precedence, wins ties).
    /// Blank scopes and scopes equal to AppName inside IncludeScopes are silently dropped.
    /// </summary>
    private List<string> BuildScopeList()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var deduped = _options.IncludeScopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s => !string.Equals(s, _options.AppName, StringComparison.OrdinalIgnoreCase))
            .Where(seen.Add)
            .ToList();

        deduped.Add(_options.AppName);
        return deduped;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _timer?.Dispose();
    }
}
