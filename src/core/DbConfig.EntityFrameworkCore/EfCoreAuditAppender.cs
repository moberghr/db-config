using DbConfig.Core;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Appender that enlists each audit row in the same <see cref="DbConfigDbContext"/> as the
/// mutation that produced it. The row is added to <see cref="DbConfigDbContext.AuditEntries"/>
/// but not saved — the calling <see cref="EfCoreConfigStore"/> commits both the entry mutation
/// and the audit row in a single <c>SaveChangesAsync</c>. This preserves the §0.7 invariant
/// that audit rows never diverge from the database state they describe.
/// </summary>
/// <remarks>
/// Each instance is bound to a single per-call <see cref="DbConfigDbContext"/>. The store
/// constructs a fresh appender inside every mutation method; the appender is discarded when
/// the context is disposed.
/// </remarks>
internal sealed class EfCoreAuditAppender : IConfigAuditAppender
{
    private readonly DbConfigDbContext _context;

    public EfCoreAuditAppender(DbConfigDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async ValueTask AppendAsync(ConfigAuditEntryRecord row, CancellationToken ct)
    {
        await _context.AuditEntries.AddAsync(MapToEntity(row), ct).ConfigureAwait(false);
    }

    private static AuditEntry MapToEntity(ConfigAuditEntryRecord row)
    {
        return new AuditEntry
        {
            Id = row.Id,
            Scope = row.Scope,
            Environment = row.Environment,
            TenantId = row.TenantId,
            Key = row.Key,
            OldValue = row.OldValue,
            NewValue = row.NewValue,
            IsSecret = row.IsSecret,
            Action = row.Action.ToString(),
            ModifiedUtc = row.ModifiedUtc,
            ModifiedBy = row.ModifiedBy,
        };
    }
}
