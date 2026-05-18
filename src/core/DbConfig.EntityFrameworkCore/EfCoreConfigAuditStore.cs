using DbConfig.Core;
using Microsoft.EntityFrameworkCore;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IConfigAuditStore"/>. Queries the
/// <c>DbConfig_AuditEntries</c> table and decrypts secret values before returning them to callers.
/// </summary>
public sealed class EfCoreConfigAuditStore : IConfigAuditStore
{
    private readonly IDbContextFactory<DbConfigDbContext> _factory;
    private readonly IConfigEncryptor _encryptor;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreConfigAuditStore"/> class.
    /// </summary>
    /// <param name="factory">EF Core context factory.</param>
    /// <param name="encryptor">Encryptor used to decrypt secret values before returning them.</param>
    public EfCoreConfigAuditStore(
        IDbContextFactory<DbConfigDbContext> factory,
        IConfigEncryptor encryptor)
    {
        _factory = factory;
        _encryptor = encryptor;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(ConfigAuditEntry entry, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entity = new ConfigAuditEntryEntity
        {
            Id = entry.Id,
            AppName = entry.AppName,
            Environment = entry.Environment,
            TenantId = entry.TenantId,
            Key = entry.Key,
            OldValue = entry.OldValue,
            NewValue = entry.NewValue,
            IsSecret = entry.IsSecret,
            Action = entry.Action.ToString(),
            ModifiedUtc = entry.ModifiedUtc,
            ModifiedBy = entry.ModifiedBy,
        };

        context.AuditEntries.Add(entity);
        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryAsync(
        string appName, string environment, string key, int take, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var rows = await context.AuditEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName && x.Environment == environment && x.TenantId == string.Empty && x.Key == key)
            .OrderByDescending(x => x.ModifiedUtc)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.AppName,
                x.Environment,
                x.TenantId,
                x.Key,
                x.OldValue,
                x.NewValue,
                x.IsSecret,
                x.Action,
                x.ModifiedUtc,
                x.ModifiedBy,
            })
            .ToListAsync(ct);

        return rows.ConvertAll(row =>
        {
            var oldValue = row.IsSecret && row.OldValue is not null
                ? _encryptor.Unprotect(row.OldValue)
                : row.OldValue;

            var newValue = row.IsSecret && row.NewValue is not null
                ? _encryptor.Unprotect(row.NewValue)
                : row.NewValue;

            var action = Enum.Parse<ConfigAuditAction>(row.Action);

            return new ConfigAuditEntry(
                row.Id,
                row.AppName,
                row.Environment,
                row.TenantId,
                row.Key,
                OldValue: oldValue,
                NewValue: newValue,
                IsSecret: row.IsSecret,
                Action: action,
                ModifiedUtc: row.ModifiedUtc,
                ModifiedBy: row.ModifiedBy);
        });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigAuditEntry>> GetHistoryForTenantAsync(
        string appName, string environment, string tenantId, string key, int take, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var rows = await context.AuditEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName && x.Environment == environment && x.TenantId == tenantId && x.Key == key)
            .OrderByDescending(x => x.ModifiedUtc)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.AppName,
                x.Environment,
                x.TenantId,
                x.Key,
                x.OldValue,
                x.NewValue,
                x.IsSecret,
                x.Action,
                x.ModifiedUtc,
                x.ModifiedBy,
            })
            .ToListAsync(ct);

        return rows.ConvertAll(row =>
        {
            var oldValue = row.IsSecret && row.OldValue is not null
                ? _encryptor.Unprotect(row.OldValue)
                : row.OldValue;

            var newValue = row.IsSecret && row.NewValue is not null
                ? _encryptor.Unprotect(row.NewValue)
                : row.NewValue;

            var action = Enum.Parse<ConfigAuditAction>(row.Action);

            return new ConfigAuditEntry(
                row.Id,
                row.AppName,
                row.Environment,
                row.TenantId,
                row.Key,
                OldValue: oldValue,
                NewValue: newValue,
                IsSecret: row.IsSecret,
                Action: action,
                ModifiedUtc: row.ModifiedUtc,
                ModifiedBy: row.ModifiedBy);
        });
    }
}
