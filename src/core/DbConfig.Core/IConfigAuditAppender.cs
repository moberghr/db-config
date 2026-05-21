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
/// <para>
/// <strong>Lock-held synchronous contract (in-memory store only):</strong>
/// <see cref="InMemoryConfigStore"/> invokes <see cref="AppendAsync"/> from inside a
/// <c>lock</c> covering its entry dictionary, bridging the returned <c>ValueTask</c>
/// synchronously. Implementations consumed by the in-memory store therefore MUST complete
/// synchronously — no real I/O, no await on a never-completing task — or the calling thread
/// will block the entire store and any concurrent reader. The shipped
/// <see cref="NoOpConfigAuditAppender"/> and <see cref="InMemoryConfigAuditAppender"/> are
/// both fully synchronous. The EF-backed appender does perform a real async operation
/// (<c>DbSet.AddAsync</c>) but is never invoked under the in-memory lock — it runs inside
/// <c>EfCoreConfigStore</c>, which holds no in-process lock across the call.
/// </para>
/// </remarks>
public interface IConfigAuditAppender
{
    /// <summary>
    /// Records <paramref name="row"/> alongside the caller's pending commit.
    /// </summary>
    ValueTask AppendAsync(ConfigAuditEntryRecord row, CancellationToken ct);
}
