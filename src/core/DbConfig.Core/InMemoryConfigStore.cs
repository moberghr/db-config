namespace DbConfig.Core;

/// <summary>
/// Public testing utility — register manually for tests that exercise the configuration
/// provider or HTTP endpoints over an in-process store. Not for production.
/// </summary>
public sealed class InMemoryConfigStore : IConfigStore
{
    // Key: (AppName, Environment, TenantId, Key) — stored case-insensitively on Key; TenantId is case-sensitive.
    private readonly Dictionary<(string AppName, string Environment, string TenantId, string Key), ConfigEntry> _entries = [];
    private readonly object _lock = new();
    private readonly IConfigEncryptor _encryptor;
    private readonly InMemoryConfigAuditStore? _auditStore;
    private readonly bool _enableAuditLog;

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
    {
        _encryptor = encryptor ?? new PassthroughConfigEncryptor();
        _auditStore = auditStore;
        _enableAuditLog = enableAuditLog;
    }

    /// <summary>
    /// Number of times <see cref="GetAllAsync"/> has been called on this instance.
    /// Useful in tests that verify an endpoint does not perform a full-scope scan.
    /// </summary>
    public int GetAllAsyncCallCount { get; private set; }

    /// <summary>
    /// Number of times <see cref="GetAsync"/> has been called on this instance.
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

    public Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllAsyncCallCount++;

            var result = _entries.Values
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.TenantId == string.Empty)
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntry>>(result);
        }
    }

    public Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAsyncCallCount++;

            var storeKey = (appName, environment, string.Empty, key);
            _entries.TryGetValue(storeKey, out var entry);
            return Task.FromResult(entry is null ? null : DecryptEntry(entry));
        }
    }

    public Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string appName, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            var entries = _entries.Values
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
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

    public Task UpsertAsync(ConfigEntry entry, CancellationToken ct)
    {
        var key = (entry.AppName, entry.Environment, entry.TenantId, entry.Key);
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
                _auditStore.Add(new ConfigAuditEntry(
                    Guid.NewGuid(),
                    entry.AppName,
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

    public Task DeleteAsync(string appName, string environment, string key, CancellationToken ct)
    {
        var storeKey = (appName, environment, string.Empty, key);

        lock (_lock)
        {
            if (_entries.TryGetValue(storeKey, out var existing))
            {
                _entries.Remove(storeKey);

                if (_enableAuditLog && _auditStore is not null)
                {
                    _auditStore.Add(new ConfigAuditEntry(
                        Guid.NewGuid(),
                        appName,
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

    public Task<IReadOnlyList<ConfigEntry>> GetAllScopedAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllScopedAsyncCallCount++;

            // Return entries in the same order as the input appNames list so that
            // callers can rely on precedence iteration order (last element wins per key).
            // Only global (TenantId = "") entries are returned — tenant-aware callers use GetAllForTenantAsync.
            var result = appNames
                .SelectMany(appName => _entries.Values
                    .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.TenantId == string.Empty)
                    .Select(DecryptEntry))
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntry>>(result);
        }
    }

    public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetLatestModifiedUtcScopedAsyncCallCount++;

            var entries = _entries.Values
                .Where(x => appNames.Any(a => string.Equals(x.AppName, a, StringComparison.OrdinalIgnoreCase)))
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
    public Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct)
    {
        lock (_lock)
        {
            var result = _entries.Values
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.TenantId, tenantId, StringComparison.Ordinal))
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntry>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<ConfigEntry?> GetForTenantAsync(
        string appName, string environment, string tenantId, string key, CancellationToken ct)
    {
        lock (_lock)
        {
            var storeKey = (appName, environment, tenantId, key);
            _entries.TryGetValue(storeKey, out var entry);
            return Task.FromResult(entry is null ? null : DecryptEntry(entry));
        }
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct)
    {
        lock (_lock)
        {
            var entries = _entries.Values
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
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
        string appName, string environment, string tenantId, string key, CancellationToken ct)
    {
        var storeKey = (appName, environment, tenantId, key);

        lock (_lock)
        {
            if (_entries.TryGetValue(storeKey, out var existing))
            {
                _entries.Remove(storeKey);

                if (_enableAuditLog && _auditStore is not null)
                {
                    _auditStore.Add(new ConfigAuditEntry(
                        Guid.NewGuid(),
                        appName,
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
    public Task<IReadOnlyList<ConfigEntry>> GetAllForAllTenantsAsync(
        string appName, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            var result = _entries.Values
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Select(DecryptEntry)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntry>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string appName, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            var entries = _entries.Values
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
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
    public Task<IReadOnlyList<ConfigEntry>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetAllScopedForAllTenantsAsyncCallCount++;

            // Return entries in the same order as the input appNames list so that
            // callers can rely on precedence iteration order (last element wins per (tenant, key)).
            // ALL tenants (including global TenantId = "") are included.
            var result = appNames
                .SelectMany(appName => _entries.Values
                    .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase))
                    .Select(DecryptEntry))
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigEntry>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        lock (_lock)
        {
            GetLatestModifiedUtcScopedAcrossAllTenantsAsyncCallCount++;

            var entries = _entries.Values
                .Where(x => appNames.Any(a => string.Equals(x.AppName, a, StringComparison.OrdinalIgnoreCase)))
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

    private ConfigEntry EncryptEntry(ConfigEntry entry)
    {
        if (!entry.IsSecret || entry.Value is null)
        {
            return entry;
        }

        return entry with { Value = _encryptor.Protect(entry.Value) };
    }

    private ConfigEntry DecryptEntry(ConfigEntry entry)
    {
        if (!entry.IsSecret || entry.Value is null)
        {
            return entry;
        }

        return entry with { Value = _encryptor.Unprotect(entry.Value) };
    }
}
