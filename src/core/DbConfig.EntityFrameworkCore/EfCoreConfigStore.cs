using DbConfig.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// <see cref="IConfigStore"/> implementation backed by EF Core.
/// Registered as Singleton; creates a per-call <see cref="DbConfigDbContext"/>
/// via <see cref="IDbContextFactory{TContext}"/> to avoid shared change tracking.
/// </summary>
public sealed class EfCoreConfigStore : IConfigStore
{
    private readonly IDbContextFactory<DbConfigDbContext> _factory;
    private readonly IUniqueConstraintDetector _detector;
    private readonly TimeProvider _timeProvider;
    private readonly IConfigEncryptor _encryptor;
    private readonly bool _enableAuditLog;
    private readonly DbConfigOptions? _options;
    private readonly ITenantResolver? _tenantResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreConfigStore"/> class.
    /// </summary>
    /// <param name="factory">EF Core context factory.</param>
    /// <param name="detector">Provider-specific unique-constraint detector for upsert retry.</param>
    /// <param name="timeProvider">Time provider for <c>ModifiedUtc</c> stamping.</param>
    /// <param name="encryptor">
    /// Optional encryptor for entries where <c>IsSecret=true</c>.
    /// When <see langword="null"/>, a <see cref="PassthroughConfigEncryptor"/> is used,
    /// which returns stored values verbatim without decryption.
    /// <para>
    /// <strong>Null-encryptor (polling-side) path:</strong> When <c>AddDbConfig</c> detects a
    /// type-mapped or factory-based <c>IConfigEncryptor</c> registration, it constructs the
    /// polling-side store with <see langword="null"/> here. In this mode <c>GetAllAsync</c> and
    /// <c>GetAsync</c> return raw stored values — ciphertext for <c>IsSecret=true</c> rows.
    /// The <see cref="DbConfigConfigurationProvider"/> holds those raw values and decrypts
    /// lazily via <c>TryGet</c> once <c>DbConfigEncryptorActivator</c> calls
    /// <c>SetEncryptor</c> after host construction completes. The HTTP-side store always
    /// has a real encryptor resolved from DI and decrypts normally.
    /// </para>
    /// </param>
    /// <param name="enableAuditLog">
    /// When <see langword="true"/> (the default), every Upsert and Delete writes an audit row
    /// in the same <c>SaveChangesAsync</c> as the mutation. Pass <see langword="false"/> to opt
    /// out entirely (no rows written to <c>DbConfig_AuditEntries</c>).
    /// </param>
    public EfCoreConfigStore(
        IDbContextFactory<DbConfigDbContext> factory,
        IUniqueConstraintDetector detector,
        TimeProvider timeProvider,
        IConfigEncryptor? encryptor = null,
        bool enableAuditLog = true)
        : this(factory, detector, timeProvider, encryptor, enableAuditLog, options: null, tenantResolver: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreConfigStore"/> class with ambient
    /// <see cref="DbConfigOptions"/> and an optional <see cref="ITenantResolver"/>.
    /// The HTTP-side store registration uses this overload so the convenience overloads
    /// (<c>GetAsync(key)</c>, <c>GetAsync&lt;T&gt;()</c>, etc.) can resolve ambient
    /// AppName/Environment and the current tenant id without callers passing them on
    /// every call.
    /// </summary>
    /// <param name="factory">EF Core context factory.</param>
    /// <param name="detector">Provider-specific unique-constraint detector for upsert retry.</param>
    /// <param name="timeProvider">Time provider for <c>ModifiedUtc</c> stamping.</param>
    /// <param name="options">
    /// The host's <see cref="DbConfigOptions"/>. Required for the convenience overloads;
    /// the explicit-app/env API methods do not consult it.
    /// </param>
    /// <param name="encryptor">Optional encryptor (same semantics as the legacy constructor).</param>
    /// <param name="enableAuditLog">Audit log toggle (same semantics as the legacy constructor).</param>
    /// <param name="tenantResolver">
    /// Optional tenant resolver. When provided, <c>GetAsync(key)</c> / <c>GetAllAsync()</c>
    /// pick the tenant returned by <see cref="ITenantResolver.Resolve"/>; when null, those
    /// overloads behave as "global only".
    /// </param>
    public EfCoreConfigStore(
        IDbContextFactory<DbConfigDbContext> factory,
        IUniqueConstraintDetector detector,
        TimeProvider timeProvider,
        DbConfigOptions options,
        IConfigEncryptor? encryptor = null,
        bool enableAuditLog = true,
        ITenantResolver? tenantResolver = null)
        : this(
            factory,
            detector,
            timeProvider,
            encryptor,
            enableAuditLog,
            options ?? throw new ArgumentNullException(nameof(options)),
            tenantResolver)
    {
    }

    private EfCoreConfigStore(
        IDbContextFactory<DbConfigDbContext> factory,
        IUniqueConstraintDetector detector,
        TimeProvider timeProvider,
        IConfigEncryptor? encryptor,
        bool enableAuditLog,
        DbConfigOptions? options,
        ITenantResolver? tenantResolver)
    {
        _factory = factory;
        _detector = detector;
        _timeProvider = timeProvider;
        _encryptor = encryptor ?? new PassthroughConfigEncryptor();
        _enableAuditLog = enableAuditLog;
        _options = options;
        _tenantResolver = tenantResolver;
    }

    public async Task<IReadOnlyList<ConfigEntry>> GetAllAsync(string appName, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntry>> GetAllAsync(CancellationToken ct)
    {
        var options = RequireOptions();
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrEmpty(tenantId))
        {
            return GetAllAsync(options.AppName, options.Environment, ct);
        }

        return GetAllForTenantAsync(options.AppName, options.Environment, tenantId, ct);
    }

    public async Task<ConfigEntry?> GetAsync(string appName, string environment, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entity = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Where(x => x.Key == key)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : DecryptEntry(entity);
    }

    /// <inheritdoc/>
    public Task<ConfigEntry?> GetAsync(string key, CancellationToken ct)
    {
        var options = RequireOptions();
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrEmpty(tenantId))
        {
            return GetAsync(options.AppName, options.Environment, key, ct);
        }

        return GetForTenantAsync(options.AppName, options.Environment, tenantId, key, ct);
    }

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(CancellationToken ct)
        where T : class, new()
    {
        var tenantId = _tenantResolver?.Resolve() ?? string.Empty;

        return await BindTypedAsync<T>(tenantId, ct).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string appName, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x => (DateTime?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return new DateTimeOffset(latestModifiedUtc.Value, TimeSpan.Zero);
    }

    public async Task UpsertAsync(ConfigEntry entry, CancellationToken ct)
    {
        var modifiedUtc = entry.ModifiedUtc == default
            ? _timeProvider.GetUtcNow().UtcDateTime
            : entry.ModifiedUtc.UtcDateTime;

        // Encrypt the value before persisting when the entry is marked as a secret.
        var storedValue = entry.IsSecret && entry.Value is not null
            ? _encryptor.Protect(entry.Value)
            : entry.Value;

        // Single-retry loop: on a concurrent insert race one writer may hit a
        // unique-constraint violation. We catch DbUpdateException, delegate to the
        // injected IUniqueConstraintDetector to identify the error, and retry
        // once as an update. Two iterations are sufficient; the second pass reads the
        // now-existing row and applies an update.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            // Capture the old stored value BEFORE applying the mutation (for audit).
            var existing = await context.ConfigEntries
                .Where(x => x.AppName == entry.AppName)
                .Where(x => x.Environment == entry.Environment)
                .Where(x => x.TenantId == entry.TenantId)
                .Where(x => x.Key == entry.Key)
                .FirstOrDefaultAsync(ct);

            if (existing is null)
            {
                var newEntity = new ConfigEntryEntity
                {
                    Id = Guid.NewGuid(),
                    AppName = entry.AppName,
                    Environment = entry.Environment,
                    TenantId = entry.TenantId,
                    Key = entry.Key,
                    Value = storedValue,
                    IsSecret = entry.IsSecret,
                    ModifiedUtc = modifiedUtc,
                    ModifiedBy = entry.ModifiedBy,
                };

                await context.ConfigEntries.AddAsync(newEntity, ct);

                if (_enableAuditLog)
                {
                    await context.AuditEntries.AddAsync(
                        BuildAuditEntity(
                            entry,
                            ConfigAuditAction.Insert,
                            oldValue: null,
                            newValue: storedValue,
                            modifiedUtc),
                        ct);
                }
            }
            else
            {
                var oldStoredValue = existing.Value; // capture BEFORE mutation

                existing.Value = storedValue;
                existing.IsSecret = entry.IsSecret;
                existing.ModifiedUtc = modifiedUtc;
                existing.ModifiedBy = entry.ModifiedBy;

                if (_enableAuditLog)
                {
                    await context.AuditEntries.AddAsync(
                        BuildAuditEntity(
                            entry,
                            ConfigAuditAction.Update,
                            oldValue: oldStoredValue,
                            newValue: storedValue,
                            modifiedUtc),
                        ct);
                }
            }

            try
            {
                await context.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt == 0 && _detector.IsUniqueConstraintViolation(ex))
            {
                // Lost an insert race — retry once as an update by looping back.
            }
        }
    }

    public async Task DeleteAsync(string appName, string environment, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var existing = await context.ConfigEntries
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Where(x => x.Key == key)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return;
        }

        var oldStoredValue = existing.Value; // capture BEFORE removal

        context.ConfigEntries.Remove(existing);

        if (_enableAuditLog)
        {
            var modifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;

            await context.AuditEntries.AddAsync(
                new ConfigAuditEntryEntity
                {
                    Id = Guid.NewGuid(),
                    AppName = appName,
                    Environment = environment,
                    TenantId = string.Empty,
                    Key = key,
                    OldValue = oldStoredValue,
                    NewValue = null,
                    IsSecret = existing.IsSecret,
                    Action = ConfigAuditAction.Delete.ToString(),
                    ModifiedUtc = new DateTimeOffset(modifiedUtc, TimeSpan.Zero),
                    ModifiedBy = null,
                },
                ct);
        }

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntry>> GetAllScopedAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => appNames.Contains(x.AppName))
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .ToListAsync(ct);

        var decrypted = entities.ConvertAll(DecryptEntry);

        // Re-order to match the input appNames list so precedence iteration is stable.
        return [.. appNames.SelectMany(appName => decrypted.Where(e => string.Equals(e.AppName, appName, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => appNames.Contains(x.AppName))
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x => (DateTime?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return new DateTimeOffset(latestModifiedUtc.Value, TimeSpan.Zero);
    }

    // -------------------------------------------------------------------------
    // Tenant-aware overloads (B54)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntry>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var options = RequireOptions();

        return GetAllForTenantAsync(options.AppName, options.Environment, tenantId, ct);
    }

    /// <inheritdoc/>
    public async Task<ConfigEntry?> GetForTenantAsync(
        string appName, string environment, string tenantId, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entity = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.Key == key)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : DecryptEntry(entity);
    }

    /// <inheritdoc/>
    public Task<ConfigEntry?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var options = RequireOptions();

        return GetForTenantAsync(options.AppName, options.Environment, tenantId, key, ct);
    }

    /// <inheritdoc/>
    public async Task<T> GetForTenantAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        return await BindTypedAsync<T>(tenantId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcForTenantAsync(
        string appName, string environment, string tenantId, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Select(x => (DateTime?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return new DateTimeOffset(latestModifiedUtc.Value, TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public async Task DeleteForTenantAsync(
        string appName, string environment, string tenantId, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var existing = await context.ConfigEntries
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.Key == key)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return;
        }

        var oldStoredValue = existing.Value; // capture BEFORE removal

        context.ConfigEntries.Remove(existing);

        if (_enableAuditLog)
        {
            var modifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;

            await context.AuditEntries.AddAsync(
                new ConfigAuditEntryEntity
                {
                    Id = Guid.NewGuid(),
                    AppName = appName,
                    Environment = environment,
                    TenantId = tenantId,
                    Key = key,
                    OldValue = oldStoredValue,
                    NewValue = null,
                    IsSecret = existing.IsSecret,
                    Action = ConfigAuditAction.Delete.ToString(),
                    ModifiedUtc = new DateTimeOffset(modifiedUtc, TimeSpan.Zero),
                    ModifiedBy = null,
                },
                ct);
        }

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntry>> GetAllForAllTenantsAsync(
        string appName, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string appName, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.AppName == appName)
            .Where(x => x.Environment == environment)
            .Select(x => (DateTime?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return new DateTimeOffset(latestModifiedUtc.Value, TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntry>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => appNames.Contains(x.AppName))
            .Where(x => x.Environment == environment)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .ToListAsync(ct);

        var decrypted = entities.ConvertAll(DecryptEntry);

        // Re-order to match the input appNames list so precedence iteration is stable.
        return [.. appNames.SelectMany(appName => decrypted.Where(e => string.Equals(e.AppName, appName, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> appNames, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => appNames.Contains(x.AppName))
            .Where(x => x.Environment == environment)
            .Select(x => (DateTime?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return new DateTimeOffset(latestModifiedUtc.Value, TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntry>> QueryAsync(
        string? appName,
        string? environment,
        string? tenantId,
        string? keyPrefix,
        int take,
        CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var query = context.ConfigEntries
            .AsNoTracking()
            .AsQueryable();

        if (appName is not null)
        {
            // Server-side equality. Matches the existing EF Core convention in this store
            // (GetAllAsync, GetAsync, etc.) — the columns carry case-sensitive collation
            // post-v0.5.0, so this comparison is effectively ordinal. The InMemory store
            // uses OrdinalIgnoreCase for parity with the legacy app-name convention; the
            // discrepancy is documented in §8.14 and aligns with how real production
            // databases would resolve the lookup.
            query = query.Where(x => x.AppName == appName);
        }

        if (environment is not null)
        {
            query = query.Where(x => x.Environment == environment);
        }

        if (tenantId is not null)
        {
            // Tenants are case-sensitive (§8.14). Use the column's native collation.
            query = query.Where(x => x.TenantId == tenantId);
        }

        if (keyPrefix is not null)
        {
            // Use EF.Functions.Like to issue a server-side LIKE 'prefix%'. The Key column
            // post-v0.5.0 carries a case-sensitive collation; the InMemory store does
            // OrdinalIgnoreCase prefix-matching, so callers that need strict UI parity
            // must lowercase their seed data. See §8.14.
            var pattern = EscapeLikePattern(keyPrefix) + "%";
            query = query.Where(x => EF.Functions.Like(x.Key, pattern));
        }

        var entities = await query
            .OrderBy(x => x.AppName)
            .ThenBy(x => x.Environment)
            .ThenBy(x => x.TenantId)
            .ThenBy(x => x.Key)
            .Take(take)
            .Select(x =>
                new ConfigEntry(
                    x.AppName,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    new DateTimeOffset(x.ModifiedUtc, TimeSpan.Zero),
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
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
                "This EfCoreConfigStore was constructed without DbConfigOptions. The convenience "
                + "overloads (implicit app/env) require options to be supplied via the DI-friendly "
                + "constructor. Use the explicit-app/env overload, or construct the store via DI.");
        }

        return _options;
    }

    private async Task<T> BindTypedAsync<T>(string tenantId, CancellationToken ct)
        where T : class, new()
    {
        var options = RequireOptions();
        var prefix = typeof(T).Name + ":";

        // Global layer first, then tenant overrides on top.
        var globals = await GetAllAsync(options.AppName, options.Environment, ct).ConfigureAwait(false);
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var globalMatches = globals
            .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in globalMatches)
        {
            merged[entry.Key[prefix.Length..]] = entry.Value;
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantEntries = await GetAllForTenantAsync(options.AppName, options.Environment, tenantId, ct).ConfigureAwait(false);

            var tenantMatches = tenantEntries
                .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in tenantMatches)
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

    private ConfigEntry DecryptEntry(ConfigEntry entry)
    {
        if (!entry.IsSecret || entry.Value is null)
        {
            return entry;
        }

        return entry with { Value = _encryptor.Unprotect(entry.Value) };
    }

    /// <summary>
    /// Escapes the SQL LIKE wildcard characters (<c>%</c> and <c>_</c>) so a caller-supplied
    /// prefix string is matched literally. EF Core does not auto-escape LIKE input.
    /// </summary>
    private static string EscapeLikePattern(string input)
    {
        // Order matters: escape backslash first so we don't double-escape the escapes below.
        // Then escape the wildcard characters. The resulting pattern is paired with EF.Functions.Like
        // which uses ESCAPE '\' by convention on both providers.
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static ConfigAuditEntryEntity BuildAuditEntity(
        ConfigEntry entry,
        ConfigAuditAction action,
        string? oldValue,
        string? newValue,
        DateTime modifiedUtc)
    {
        return new ConfigAuditEntryEntity
        {
            Id = Guid.NewGuid(),
            AppName = entry.AppName,
            Environment = entry.Environment,
            TenantId = entry.TenantId,
            Key = entry.Key,
            OldValue = oldValue,
            NewValue = newValue,
            IsSecret = entry.IsSecret,
            Action = action.ToString(),
            ModifiedUtc = new DateTimeOffset(modifiedUtc, TimeSpan.Zero),
            ModifiedBy = entry.ModifiedBy,
        };
    }
}
