namespace DbConfig.Core;

/// <summary>
/// In-memory implementation of <see cref="IConfigAuditStore"/> for use in tests.
/// Not for production use.
/// </summary>
public sealed class InMemoryConfigAuditStore : IConfigAuditStore
{
    private readonly List<ConfigAuditEntry> _entries = [];
    private readonly object _lock = new();

    /// <summary>
    /// All audit entries that have been written, in insertion order.
    /// Thread-safe snapshot at the time of access.
    /// </summary>
    public IReadOnlyList<ConfigAuditEntry> AllEntries
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
    internal void Add(ConfigAuditEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    /// <inheritdoc/>
    public Task WriteAsync(ConfigAuditEntry entry, CancellationToken ct)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryAsync(
        string appName, string environment, string key, int take, CancellationToken ct)
    {
        lock (_lock)
        {
            // Legacy method: returns only global (TenantId = "") audit entries.
            var result = _entries
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase)
                    && x.TenantId == string.Empty
                    && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ModifiedUtc)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigAuditEntry>>(result);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryForTenantAsync(
        string appName, string environment, string tenantId, string key, int take, CancellationToken ct)
    {
        lock (_lock)
        {
            var result = _entries
                .Where(x => string.Equals(x.AppName, appName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.TenantId, tenantId, StringComparison.Ordinal)
                    && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ModifiedUtc)
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<ConfigAuditEntry>>(result);
        }
    }
}
