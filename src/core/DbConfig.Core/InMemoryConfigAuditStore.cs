namespace DbConfig.Core;

/// <summary>
/// In-memory implementation of <see cref="IConfigAuditStore"/> for use in tests.
/// Not for production use.
/// </summary>
public sealed class InMemoryConfigAuditStore : IConfigAuditStore
{
    private readonly List<ConfigAuditEntryRecord> _entries = [];
    private readonly object _lock = new();

    /// <summary>
    /// All audit entries that have been written, in insertion order.
    /// Thread-safe snapshot at the time of access.
    /// </summary>
    public IReadOnlyList<ConfigAuditEntryRecord> AllEntries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>
    /// Adds an audit entry. Called by <see cref="InMemoryConfigStore"/> when configured
    /// with this audit store.
    /// </summary>
    internal void Add(ConfigAuditEntryRecord entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    /// <inheritdoc/>
    public Task WriteAsync(ConfigAuditEntryRecord entry, CancellationToken ct)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigAuditEntryRecord>> GetHistoryAsync(
        string scope, string environment, string key, int take, CancellationToken ct)
    {
        lock (_lock)
        {
            // Legacy method: returns only global (TenantId = "") audit entries.
            var result = _entries
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase)
                    && x.TenantId == string.Empty
                    && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ModifiedUtc)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigAuditEntryRecord>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigAuditEntryRecord>> GetHistoryForTenantAsync(
        string scope, string environment, string tenantId, string key, int take, CancellationToken ct)
    {
        lock (_lock)
        {
            var result = _entries
                .Where(x => string.Equals(x.Scope, scope, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ModifiedUtc)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigAuditEntryRecord>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigAuditEntryRecord>> QueryAsync(
        string? scope,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        ConfigAuditAction? action,
        int take,
        CancellationToken ct)
    {
        lock (_lock)
        {
            var query = _entries.AsEnumerable();

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

            if (action is not null)
            {
                query = query.Where(x => x.Action == action.Value);
            }

            var result = query
                .OrderByDescending(x => x.ModifiedUtc)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigAuditEntryRecord>>(result);
        }
    }
}
