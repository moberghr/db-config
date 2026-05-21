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
    /// out entirely (no rows written to <c>AuditEntry</c>).
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
    /// Scope/Environment and the current tenant id without callers passing them on
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

    public async Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(string scope, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllAsync(CancellationToken ct)
    {
        var options = RequireOptions();
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return GetAllAsync(options.Scope, options.Environment, ct);
        }

        return GetAllForTenantAsync(options.Scope, options.Environment, tenantId, ct);
    }

    public async Task<ConfigEntryRecord?> GetAsync(string scope, string environment, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entity = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Where(x => x.Key == key)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : DecryptEntry(entity);
    }

    /// <inheritdoc/>
    public Task<ConfigEntryRecord?> GetAsync(string key, CancellationToken ct)
    {
        var options = RequireOptions();
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return GetAsync(options.Scope, options.Environment, key, ct);
        }

        return GetForTenantAsync(options.Scope, options.Environment, tenantId, key, ct);
    }

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(CancellationToken ct)
        where T : class, new()
    {
        var tenantId = _tenantResolver?.Resolve();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = string.Empty;
        }

        return await BindTypedAsync<T>(tenantId, ct).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLatestModifiedUtcAsync(string scope, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x => (DateTimeOffset?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return latestModifiedUtc.Value;
    }

    public async Task UpsertAsync(ConfigEntryRecord entry, CancellationToken ct)
    {
        var modifiedUtc = entry.ModifiedUtc == default
            ? _timeProvider.GetUtcNow()
            : entry.ModifiedUtc.ToUniversalTime();

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
            var auditAppender = CreateAuditAppender(context);

            // Capture the old stored value BEFORE applying the mutation (for audit).
            var existing = await context.ConfigEntries
                .Where(x => x.Scope == entry.Scope)
                .Where(x => x.Environment == entry.Environment)
                .Where(x => x.TenantId == entry.TenantId)
                .Where(x => x.Key == entry.Key)
                .FirstOrDefaultAsync(ct);

            if (existing is null)
            {
                var newEntity = new ConfigEntry
                {
                    Id = Guid.NewGuid(),
                    Scope = entry.Scope,
                    Environment = entry.Environment,
                    TenantId = entry.TenantId,
                    Key = entry.Key,
                    Value = storedValue,
                    IsSecret = entry.IsSecret,
                    ModifiedUtc = modifiedUtc,
                    ModifiedBy = entry.ModifiedBy,
                };

                await context.ConfigEntries.AddAsync(newEntity, ct);

                await auditAppender.AppendAsync(
                    BuildAuditRowForUpsert(
                        entry,
                        ConfigAuditAction.Insert,
                        oldValue: null,
                        newValue: storedValue,
                        modifiedUtc),
                    ct);
            }
            else
            {
                var oldStoredValue = existing.Value; // capture BEFORE mutation

                existing.Value = storedValue;
                existing.IsSecret = entry.IsSecret;
                existing.ModifiedUtc = modifiedUtc;
                existing.ModifiedBy = entry.ModifiedBy;

                await auditAppender.AppendAsync(
                    BuildAuditRowForUpsert(
                        entry,
                        ConfigAuditAction.Update,
                        oldValue: oldStoredValue,
                        newValue: storedValue,
                        modifiedUtc),
                    ct);
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

    public async Task DeleteAsync(string scope, string environment, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);
        var auditAppender = CreateAuditAppender(context);

        var existing = await context.ConfigEntries
            .Where(x => x.Scope == scope)
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

        await auditAppender.AppendAsync(
            BuildAuditRowForDelete(
                scope,
                environment,
                tenantId: string.Empty,
                key,
                existing.IsSecret,
                oldValue: oldStoredValue,
                _timeProvider.GetUtcNow()),
            ct);

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => scopes.Contains(x.Scope))
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .ToListAsync(ct);

        var decrypted = entities.ConvertAll(DecryptEntry);

        // Re-order to match the input scopes list so precedence iteration is stable.
        return [.. scopes.SelectMany(scope => decrypted.Where(e => string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcScopedAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => scopes.Contains(x.Scope))
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == string.Empty)
            .Select(x => (DateTimeOffset?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return latestModifiedUtc.Value;
    }

    // -------------------------------------------------------------------------
    // Tenant-aware overloads (B54)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(
        string scope, string environment, string tenantId, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConfigEntryRecord>> GetAllForTenantAsync(string tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var options = RequireOptions();

        return GetAllForTenantAsync(options.Scope, options.Environment, tenantId, ct);
    }

    /// <inheritdoc/>
    public async Task<ConfigEntryRecord?> GetForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entity = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.Key == key)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : DecryptEntry(entity);
    }

    /// <inheritdoc/>
    public Task<ConfigEntryRecord?> GetForTenantAsync(string tenantId, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var options = RequireOptions();

        return GetForTenantAsync(options.Scope, options.Environment, tenantId, key, ct);
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
        string scope, string environment, string tenantId, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Where(x => x.TenantId == tenantId)
            .Select(x => (DateTimeOffset?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return latestModifiedUtc.Value;
    }

    /// <inheritdoc/>
    public async Task DeleteForTenantAsync(
        string scope, string environment, string tenantId, string key, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);
        var auditAppender = CreateAuditAppender(context);

        var existing = await context.ConfigEntries
            .Where(x => x.Scope == scope)
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

        await auditAppender.AppendAsync(
            BuildAuditRowForDelete(
                scope,
                environment,
                tenantId,
                key,
                existing.IsSecret,
                oldValue: oldStoredValue,
                _timeProvider.GetUtcNow()),
            ct);

        await context.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntryRecord>> GetAllForAllTenantsAsync(
        string scope, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .ToListAsync(ct);

        return [.. entities.Select(DecryptEntry)];
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcAcrossAllTenantsAsync(
        string scope, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => x.Scope == scope)
            .Where(x => x.Environment == environment)
            .Select(x => (DateTimeOffset?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return latestModifiedUtc.Value;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntryRecord>> GetAllScopedForAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var entities = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => scopes.Contains(x.Scope))
            .Where(x => x.Environment == environment)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
                    x.ModifiedBy))
            .ToListAsync(ct);

        var decrypted = entities.ConvertAll(DecryptEntry);

        // Re-order to match the input scopes list so precedence iteration is stable.
        return [.. scopes.SelectMany(scope => decrypted.Where(e => string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLatestModifiedUtcScopedAcrossAllTenantsAsync(
        IReadOnlyList<string> scopes, string environment, CancellationToken ct)
    {
        await using var context = await _factory.CreateDbContextAsync(ct);

        var latestModifiedUtc = await context.ConfigEntries
            .AsNoTracking()
            .Where(x => scopes.Contains(x.Scope))
            .Where(x => x.Environment == environment)
            .Select(x => (DateTimeOffset?)x.ModifiedUtc)
            .MaxAsync(ct);

        if (latestModifiedUtc is null)
        {
            return null;
        }

        return latestModifiedUtc.Value;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigEntryRecord>> QueryAsync(
        string? scope,
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

        if (scope is not null)
        {
            // Server-side equality. Matches the existing EF Core convention in this store
            // (GetAllAsync, GetAsync, etc.) — the columns carry case-sensitive collation
            // post-v0.5.0, so this comparison is effectively ordinal. The InMemory store
            // uses OrdinalIgnoreCase for parity with the legacy app-name convention; the
            // discrepancy is documented in §8.14 and aligns with how real production
            // databases would resolve the lookup.
            query = query.Where(x => x.Scope == scope);
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
            .OrderBy(x => x.Scope)
            .ThenBy(x => x.Environment)
            .ThenBy(x => x.TenantId)
            .ThenBy(x => x.Key)
            .Take(take)
            .Select(x =>
                new ConfigEntryRecord(
                    x.Scope,
                    x.Environment,
                    x.TenantId,
                    x.Key,
                    x.Value,
                    x.IsSecret,
                    x.ModifiedUtc,
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
        var prefix = TypedSectionPrefix.For<T>();

        // Use QueryAsync with the section prefix so SQL filters server-side via LIKE.
        // Previously this method called GetAllAsync + GetAllForTenantAsync which scanned
        // the entire (Scope, Environment) scope on every typed bind. Pass int.MaxValue —
        // clamping is the HTTP endpoint's job, not the store layer's.
        var globals = await QueryAsync(
            scope: options.Scope,
            environment: options.Environment,
            tenantId: string.Empty,
            keyPrefix: prefix,
            take: int.MaxValue,
            ct).ConfigureAwait(false);

        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in globals)
        {
            merged[entry.Key[prefix.Length..]] = entry.Value;
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            var tenantEntries = await QueryAsync(
                scope: options.Scope,
                environment: options.Environment,
                tenantId: tenantId,
                keyPrefix: prefix,
                take: int.MaxValue,
                ct).ConfigureAwait(false);

            foreach (var entry in tenantEntries)
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

    private ConfigEntryRecord DecryptEntry(ConfigEntryRecord entry)
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

    private IConfigAuditAppender CreateAuditAppender(DbConfigDbContext context)
    {
        return _enableAuditLog
            ? new EfCoreAuditAppender(context)
            : NoOpConfigAuditAppender.Instance;
    }

    private static ConfigAuditEntryRecord BuildAuditRowForUpsert(
        ConfigEntryRecord entry,
        ConfigAuditAction action,
        string? oldValue,
        string? newValue,
        DateTimeOffset modifiedUtc)
    {
        return new ConfigAuditEntryRecord(
            Id: Guid.NewGuid(),
            Scope: entry.Scope,
            Environment: entry.Environment,
            TenantId: entry.TenantId,
            Key: entry.Key,
            OldValue: oldValue,
            NewValue: newValue,
            IsSecret: entry.IsSecret,
            Action: action,
            ModifiedUtc: modifiedUtc,
            ModifiedBy: entry.ModifiedBy);
    }

    private static ConfigAuditEntryRecord BuildAuditRowForDelete(
        string scope,
        string environment,
        string tenantId,
        string key,
        bool isSecret,
        string? oldValue,
        DateTimeOffset modifiedUtc)
    {
        return new ConfigAuditEntryRecord(
            Id: Guid.NewGuid(),
            Scope: scope,
            Environment: environment,
            TenantId: tenantId,
            Key: key,
            OldValue: oldValue,
            NewValue: null,
            IsSecret: isSecret,
            Action: ConfigAuditAction.Delete,
            ModifiedUtc: modifiedUtc,
            ModifiedBy: null);
    }
}
