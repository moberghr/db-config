using Microsoft.Extensions.Configuration;

namespace DbConfig.Core;

/// <summary>
/// Public testing utility — register manually for tests that exercise the configuration
/// provider or HTTP endpoints over an in-process store. Not for production.
/// </summary>
public sealed class InMemoryConfigStore : IConfigStore
{
    // Key: (Scope, Environment, TenantId, Key) — stored case-insensitively on Key; TenantId is case-sensitive.
    private readonly Dictionary<(string Scope, string Environment, string TenantId, string Key), ConfigEntryRecord> _entries = [];
    private readonly object _lock = new();
    private readonly IConfigEncryptor _encryptor;
    private readonly InMemoryConfigAuditStore? _auditStore;
    private readonly bool _enableAuditLog;
    private readonly DbConfigOptions? _options;
    private readonly ITenantResolver? _tenantResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryConfigStore"/> class.
    /// When <paramref name="encryptor"/> is <see langword="null"/>, a
    /// <see cref="PassthroughConfigEncryptor"/> is used (no encryption — values stored verbatim).
    /// </summary>
    /// <param name="encryptor">
    /// The encryptor used to protect and unprotect secret values.
    /// Pass <see langword="null"/> for tests that do not exercise encryption behaviour.
    /// </param>
    public InMemoryConfigStore(IConfigEncryptor? encryptor = null)
        : this(encryptor, null, enableAuditLog: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryConfigStore"/> class with an
    /// optional audit sink.
    /// </summary>
    /// <param name="encryptor">
    /// The encryptor used to protect and unprotect secret values.
    /// Pass <see langword="null"/> for tests that do not exercise encryption behaviour.
    /// </param>
    /// <param name="auditStore">
    /// When non-<see langword="null"/> and <paramref name="enableAuditLog"/> is
    /// <see langword="true"/>, each Upsert/Delete writes an audit row to this store.
    /// </param>
    /// <param name="enableAuditLog">
    /// Set to <see langword="false"/> to suppress audit row writes even when
    /// <paramref name="auditStore"/> is provided.
    /// </param>
    public InMemoryConfigStore(
        IConfigEncryptor? encryptor,
        InMemoryConfigAuditStore? auditStore,
        bool enableAuditLog = true)
        : this(encryptor, auditStore, enableAuditLog, options: null, tenantResolver: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryConfigStore"/> class with optional
    /// ambient <see cref="DbConfigOptions"/> and <see cref="ITenantResolver"/> wiring.
    /// </summary>
    /// <param name="encryptor">
    /// The encryptor used to protect and unprotect secret values, or <see langword="null"/> for
    /// the passthrough encryptor.
    /// </param>
    /// <param name="auditStore">
    /// Optional in-memory audit sink (writes occur only when <paramref name="enableAuditLog"/>
    /// is <see langword="true"/>).
    /// </param>
    /// <param name="enableAuditLog">Audit log toggle.</param>
    /// <param name="options">
    /// Optional <see cref="DbConfigOptions"/> used by the convenience overloads
    /// (<c>GetAsync(key)</c>, <c>GetAsync&lt;T&gt;()</c>, etc.). When <see langword="null"/>,
    /// the convenience overloads throw <see cref="InvalidOperationException"/>.
    /// </param>
    /// <param name="tenantResolver">
    /// Optional <see cref="ITenantResolver"/>. When provided, current-tenant convenience reads
    /// route to the tenant returned by <see cref="ITenantResolver.Resolve"/>. When
    /// <see langword="null"/>, current-tenant reads behave as "global only".
    /// </param>
    public InMemoryConfigStore(
        IConfigEncryptor? encryptor,
        InMemoryConfigAuditStore? auditStore,
        bool enableAuditLog,
        DbConfigOptions? options,
        ITenantResolver? tenantResolver)
    {
        _encryptor = encryptor ?? new PassthroughConfigEncryptor();
        _auditStore = auditStore;
        _enableAuditLog = enableAuditLog;
        _options = options;
        _tenantResolver = tenantResolver;
    }

    /// <summary>
    /// Number of times <c>GetAllAsync(scope, environment, ct)</c> has been called on this instance.
    /// Useful in tests that verify an endpoint does not perform a full-scope scan.
    /// </summary>
    public int GetAllAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <c>GetAsync(scope, environment, key, ct)</c> has been called on this instance.
    /// Useful in tests that verify an endpoint uses the targeted single-key read path.
    /// </summary>
    public int GetAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <see cref="GetAllScopedAsync"/> has been called on this instance.
    /// </summary>
    public int GetAllScopedAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <see cref="GetLatestModifiedUtcScopedAsync"/> has been called on this instance.
    /// </summary>
    public int GetLatestModifiedUtcScopedAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <see cref="GetAllScopedForAllTenantsAsync"/> has been called on this instance.
    /// </summary>
    public int GetAllScopedForAllTenantsAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <see cref="GetLatestModifiedUtcScopedAcrossAllTenantsAsync"/> has been called on this instance.
    /// </summary>
    public int GetLatestModifiedUtcScopedAcrossAllTenantsAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <see cref="QueryAsync"/> has been called on this instance.
    /// Useful in tests that verify the typed-bind / convenience overloads route through
    /// the targeted-prefix query path instead of falling back to full-scope scans.
    /// </summary>
    public int QueryAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <c>GetAllForTenantAsync(scope, environment, tenantId, ct)</c> has been
    /// called on this instance. Useful in tests that verify typed-bind / convenience overloads
    /// do not scan the entire tenant scope.
    /// </summary>
    public int GetAllForTenantAsyncCallCount { get; private set; }

    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(string scope, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllAsyncCallCount++;

            var result = _entries.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.TenantId == string.Empty)
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(CancellationToken ct)
    {
        var options = RequireOptions();
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return GetAllAsync(options.Scope, options.Environment, ct);
        }

        return GetAllForTenantAsync(options.Scope, options.Environment, tenantId, ct);
    }

    public Task<ConfigEntryRecord?> GetAsync(string scope, string environment, string key, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAsyncCallCount++;

            var storeKey = (scope, environment, string.Empty, key);
            _entries.TryGetValue(storeKey, out var entry);
            return Task.FromResult(entry is null ? null : DecryptEntry(entry));
        }
    }

    /// <inheritdoc/>
    public Task<ConfigEntryRecord?> GetAsync(string key, CancellationToken ct)
    {
        var options = RequireOptions();
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return GetAsync(options.Scope, options.Environment, key, ct);
        }

        return GetForTenantAsync(options.Scope, options.Environment, tenantId, key, ct);
    }

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(CancellationToken ct)
        where T : class, new()
    {
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = string.Empty;
        }

        return await BindTypedAsync<T>(tenantId, ct).ConfigureAwait(false);
    }

    public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            var entries = _entries.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.TenantId == string.Empty)
                .ToList();

            if (entries.Count == 0)
            {
                return Task.FromResult<DateTimeOffset?>(null);
            }

            var latest = entries.Max(x => x.ModifiedUtc);

            return Task.FromResult<DateTimeOffset?>(latest);
        }
    }

    public Task UpsertAsync(ConfigEntryRecord entry, CancellationToken ct)
    {
        var key = (entry.Scope, entry.Environment, entry.TenantId, entry.Key);
        var stored = EncryptEntry(entry);

        lock (_lock)
        {
            ConfigAuditAction action;
            string? oldValue = null;

            if (_entries.TryGetValue(key, out var existing))
            {
                action = ConfigAuditAction.Update;
                oldValue = existing.Value; // stored ciphertext or plaintext
            }
            else
            {
                action = ConfigAuditAction.Insert;
            }

            _entries[key] = stored;

            if (_enableAuditLog && _auditStore is not null)
            {
                _auditStore.Add(new ConfigAuditEntryRecord(
                    Guid.NewGuid(),
                    entry.Scope,
                    entry.Environment,
                    entry.TenantId,
                    entry.Key,
                    OldValue: oldValue,
                    NewValue: stored.Value,
                    IsSecret: entry.IsSecret,
                    Action: action,
                    ModifiedUtc: entry.ModifiedUtc == default ? DateTimeOffset.UtcNow : entry.ModifiedUtc,
                    ModifiedBy: entry.ModifiedBy));
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string scope, string environment, string key, CancellationToken ct)
    {
        var storeKey = (scope, environment, string.Empty, key);

        lock (_lock)
        {
            if (_entries.TryGetValue(storeKey, out var existing))
            {
                _entries.Remove(storeKey);

                if (_enableAuditLog && _auditStore is not null)
                {
                    _auditStore.Add(new ConfigAuditEntryRecord(
                        Guid.NewGuid(),
                        scope,
                        environment,
                        existing.TenantId,
                        key,
                        OldValue: existing.Value,
                        NewValue: null,
                        IsSecret: existing.IsSecret,
                        Action: ConfigAuditAction.Delete,
                        ModifiedUtc: DateTimeOffset.UtcNow,
                        ModifiedBy: null));
                }
            }
            else
            {
                _entries.Remove(storeKey);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllScopedAsyncCallCount++;

            // Return entries in the same order as the input scopes list so that
            // callers can rely on precedence iteration order (last element wins per key).
            // Only global (TenantId = "") entries are returned — tenant-aware callers use GetAllForTenantAsync.
            var result = scopes
                .SelectMany(scope => _entries.Values
                    .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.TenantId == string.Empty)
                    .Select(DecryptEntry))
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }
    }

    public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetLatestModifiedUtcScopedAsyncCallCount++;

            var entries = _entries.Values
                .Where(x => scopes.Any(a => string.Equals(x.Scope, a, StringComparison.OrdinalIgnoreCase)))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.TenantId == string.Empty)
                .ToList();

            if (entries.Count == 0)
            {
                return Task.FromResult<DateTimeOffset?>(null);
            }

            var latest = entries.Max(x => x.ModifiedUtc);

            return Task.FromResult<DateTimeOffset?>(latest);
        }
    }

    // -------------------------------------------------------------------------
    // Tenant-aware overloads (B54)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllForTenantAsyncCallCount++;

            var result = _entries.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var options = RequireOptions();

        return GetAllForTenantAsync(options.Scope, options.Environment, tenantId, ct);
    }

    /// <inheritdoc/>
    public Task<ConfigEntryRecord?> GetForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct)
    {
        lock (_lock)
        {
            var storeKey = (scope, environment, tenantId, key);
            _entries.TryGetValue(storeKey, out var entry);
            return Task.FromResult(entry is null ? null : DecryptEntry(entry));
        }
    }

    /// <inheritdoc/>
    public Task<ConfigEntryRecord?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var options = RequireOptions();

        return GetForTenantAsync(options.Scope, options.Environment, tenantId, key, ct);
    }

    /// <inheritdoc/>
    public async Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        return await BindTypedAsync<T>(tenantId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct)
    {
        lock (_lock)
        {
            var entries = _entries.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))
                .ToList();

            if (entries.Count == 0)
            {
                return Task.FromResult<DateTimeOffset?>(null);
            }

            var latest = entries.Max(x => x.ModifiedUtc);

            return Task.FromResult<DateTimeOffset?>(latest);
        }
    }

    /// <inheritdoc/>
    public Task DeleteForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct)
    {
        var storeKey = (scope, environment, tenantId, key);

        lock (_lock)
        {
            if (_entries.TryGetValue(storeKey, out var existing))
            {
                _entries.Remove(storeKey);

                if (_enableAuditLog && _auditStore is not null)
                {
                    _auditStore.Add(new ConfigAuditEntryRecord(
                        Guid.NewGuid(),
                        scope,
                        environment,
                        tenantId,
                        key,
                        OldValue: existing.Value,
                        NewValue: null,
                        IsSecret: existing.IsSecret,
                        Action: ConfigAuditAction.Delete,
                        ModifiedUtc: DateTimeOffset.UtcNow,
                        ModifiedBy: null));
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(
        string scope, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            var result = _entries.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string scope, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            var entries = _entries.Values
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entries.Count == 0)
            {
                return Task.FromResult<DateTimeOffset?>(null);
            }

            var latest = entries.Max(x => x.ModifiedUtc);

            return Task.FromResult<DateTimeOffset?>(latest);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllScopedForAllTenantsAsyncCallCount++;

            // Return entries in the same order as the input scopes list so that
            // callers can rely on precedence iteration order (last element wins per (tenant, key)).
            // ALL tenants (including global TenantId = "") are included.
            var result = scopes
                .SelectMany(scope => _entries.Values
                    .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                    .Select(DecryptEntry))
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetLatestModifiedUtcScopedAcrossAllTenantsAsyncCallCount++;

            var entries = _entries.Values
                .Where(x => scopes.Any(a => string.Equals(x.Scope, a, StringComparison.OrdinalIgnoreCase)))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entries.Count == 0)
            {
                return Task.FromResult<DateTimeOffset?>(null);
            }

            var latest = entries.Max(x => x.ModifiedUtc);

            return Task.FromResult<DateTimeOffset?>(latest);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> QueryAsync(
        string? scope,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        int take,
        CancellationToken ct)
    {
        lock (_lock)
        {
            QueryAsyncCallCount++;

            var query = _entries.Values.AsEnumerable();

            if (scope is not null)
            {
                query = query.Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase));
            }

            if (environment is not null)
            {
                query = query.Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase));
            }

            if (tenantId is not null)
            {
                query = query.Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal));
            }

            if (keyPrefix is not null)
            {
                query = query.Where(x => x.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
            }

            var result = query
                .OrderBy(x => x.Scope, StringComparer.Ordinal)
                .ThenBy(x => x.Environment, StringComparer.Ordinal)
                .ThenBy(x => x.TenantId, StringComparer.Ordinal)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(take)
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntryRecord>>(result);
        }
    }

    // -------------------------------------------------------------------------
    // Convenience overload helpers (v0.11.1).
    // The implicit-app/env overloads (GetAsync(key), GetAllAsync(), GetForTenantAsync(...),
    // typed Get<T>/GetForTenant<T>) are co-located with their explicit-arg siblings
    // above to satisfy S4136 "adjacent overloads". Only helper methods live here.
    // -------------------------------------------------------------------------
    private DbConfigOptions RequireOptions()
    {
        if (_options is null)
        {
            throw new InvalidOperationException(
                "This overload requires DbConfigOptions to be configured; pass options to the "
                + "InMemoryConfigStore constructor or use the explicit-app/env method.");
        }

        return _options;
    }

    private async Task<T> BindTypedAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new()
    {
        var options = RequireOptions();
        var prefix = TypedSectionPrefix.For<T>();

        // Use QueryAsync with a keyPrefix filter so the store-side filter narrows the
        // result set — avoids the previous full-scope scan that this method used to do
        // before merging in-memory. Pass int.MaxValue: clamping is the HTTP layer's job.
        var globals = await QueryAsync(
            scope: options.Scope,
            environment: options.Environment,
            tenantId: string.Empty,
            keyPrefix: prefix,
            take: int.MaxValue,
            ct).ConfigureAwait(false);

        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in globals)
        {
            merged[entry.Key[prefix.Length..]] = entry.Value;
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            var tenantEntries = await QueryAsync(
                scope: options.Scope,
                environment: options.Environment,
                tenantId: tenantId,
                keyPrefix: prefix,
                take: int.MaxValue,
                ct).ConfigureAwait(false);

            foreach (var entry in tenantEntries)
            {
                merged[entry.Key[prefix.Length..]] = entry.Value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(merged)
            .Build();

        var instance = new T();
        configuration.Bind(instance);

        return instance;
    }

    private ConfigEntryRecord EncryptEntry(ConfigEntryRecord entry)
    {
        if (!entry.IsSecret || entry.Value is null)
        {
            return entry;
        }

        return entry with { Value = _encryptor.Protect(entry.Value) };
    }

    private ConfigEntryRecord DecryptEntry(ConfigEntryRecord entry)
    {
        if (!entry.IsSecret || entry.Value is null)
        {
            return entry;
        }

        return entry with { Value = _encryptor.Unprotect(entry.Value) };
    }
}
