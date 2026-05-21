namespace DbConfig.Core;

/// <summary>
/// Appender that pushes audit rows into an <see cref="InMemoryConfigAuditStore"/>.
/// Used by <see cref="InMemoryConfigStore"/> when a backing audit store is supplied,
/// and by tests that want to assert on emitted audit content without a database.
/// </summary>
public sealed class InMemoryConfigAuditAppender : IConfigAuditAppender
{
    private readonly InMemoryConfigAuditStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryConfigAuditAppender"/> class
    /// that forwards every appended row to <paramref name="store"/>.
    /// </summary>
    public InMemoryConfigAuditAppender(InMemoryConfigAuditStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(ConfigAuditEntryRecord row, CancellationToken ct)
    {
        _store.Add(row);

        return ValueTask.CompletedTask;
    }
}
