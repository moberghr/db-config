namespace DbConfig.Core;

/// <summary>
/// Persists a single audit row produced by a mutation on the configuration store.
/// </summary>
/// <remarks>
/// <para>
/// The contract is "persist this row alongside the caller's commit". Implementations decide
/// how to honour it:
/// </para>
/// <list type="bullet">
///   <item>
///     The EF-backed store constructs an appender that enlists the row in the same
///     <c>DbContext</c> as the mutation; both rows commit atomically when the store calls
///     <c>SaveChangesAsync</c>. This preserves the §0.7 invariant that audit rows never
///     diverge from the database state they describe.
///   </item>
///   <item>
///     The in-memory store appends the row to an <see cref="InMemoryConfigAuditStore"/>
///     immediately. There is no real transaction to enlist in.
///   </item>
///   <item>
///     <see cref="NoOpConfigAuditAppender"/> discards the row — used when auditing is
///     disabled for a given store instance.
///   </item>
/// </list>
/// </remarks>
public interface IConfigAuditAppender
{
    /// <summary>
    /// Records <paramref name="row"/> alongside the caller's pending commit.
    /// </summary>
    ValueTask AppendAsync(ConfigAuditEntryRecord row, CancellationToken ct);
}
