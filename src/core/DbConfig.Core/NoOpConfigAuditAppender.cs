namespace DbConfig.Core;

/// <summary>
/// Discards every audit row passed to <see cref="AppendAsync"/>. Used by stores when
/// <c>EnableAuditLog</c> is <see langword="false"/>, and by tests that exercise mutation
/// paths without caring about audit content.
/// </summary>
public sealed class NoOpConfigAuditAppender : IConfigAuditAppender
{
    /// <summary>The shared singleton instance.</summary>
    public static IConfigAuditAppender Instance { get; } = new NoOpConfigAuditAppender();

    private NoOpConfigAuditAppender()
    {
    }

    /// <inheritdoc/>
    public ValueTask AppendAsync(ConfigAuditEntryRecord row, CancellationToken ct)
    {
        return ValueTask.CompletedTask;
    }
}
