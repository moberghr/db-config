using DbConfig.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

// Extension lives in DbConfig.EntityFrameworkCore assembly but is surfaced under
// namespace DbConfig.Core so consumers only need `using DbConfig.Core;`.
namespace DbConfig.Core;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/> (e.g. <c>WebApplicationBuilder</c>
/// and <c>HostApplicationBuilder</c>).
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Wires up the DbConfig database-backed configuration source and all required host
    /// services in a single call. Replaces the v1.0 two-call pattern.
    /// </summary>
    /// <param name="hostBuilder">The host application builder.</param>
    /// <param name="configure">
    /// Delegate that configures options and registers the store provider (e.g.
    /// <c>b.UseSqlServer(connectionString)</c>).
    /// </param>
    /// <returns>The <paramref name="hostBuilder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if called more than once, or if the configure lambda does not call a provider
    /// extension such as <c>UseSqlServer</c> or <c>UsePostgreSql</c>.
    /// </exception>
    public static IHostApplicationBuilder AddDbConfig(
        this IHostApplicationBuilder hostBuilder,
        Action<DbConfigBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ArgumentNullException.ThrowIfNull(configure);

        var existing = hostBuilder.Services.Any(
            x => x.ServiceType.Equals(typeof(DbConfigRegistrationMarker)));

        if (existing)
        {
            throw new InvalidOperationException(
                "AddDbConfig has already been called on this host. Single-scope only in v1.1.");
        }

        var options = new DbConfigOptions();
        var dbb = new DbConfigBuilder(hostBuilder.Services, options);

        configure(dbb);

        // Resolve the captured EF Core configuration action set by UseEntityFrameworkCore.
        var contextConfigObj = dbb.ConfigureDbContextActionObject
            ?? throw new InvalidOperationException(
                "AddDbConfig lambda must call a provider extension such as b.UseSqlServer(...) or b.UsePostgreSql(...) " +
                "to configure the database provider and store.");
        var contextConfig = (Action<DbContextOptionsBuilder>)contextConfigObj;

        // Resolve the captured IUniqueConstraintDetector set by the provider extension.
        var detectorObj = dbb.DetectorObject
            ?? throw new InvalidOperationException(
                "AddDbConfig lambda must call a provider extension (e.g. UseSqlServer, UsePostgreSql) " +
                "that configures the database provider.");
        var detector = (IUniqueConstraintDetector)detectorObj;

        // ---- Resolve the encryptor to use for BOTH the polling-side store and the HTTP/DI-side
        // store. The two stores must share the same encryptor instance so that ciphertext written
        // by one can be read by the other.
        //
        // Priority:
        //   1. Consumer pre-registered an INSTANCE (services.AddSingleton<IConfigEncryptor>(myImpl))
        //      — detect via ImplementationInstance; use that instance for polling side directly.
        //      This is the v1.3 path, unchanged.
        //   2. Consumer registered via type-mapping or factory — we cannot resolve it without
        //      building a ServiceProvider. Pass null to the polling-side store. Register the
        //      DbConfigEncryptorActivator hosted service to inject the encryptor post-build.
        //   3. No registration yet — construct the default DataProtection-backed encryptor and
        //      register it as AddSingleton so DI sees the same object.
        IConfigEncryptor? pollingEncryptor;
        bool needsActivator;

        var existingEncryptorDescriptor = hostBuilder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(IConfigEncryptor));

        if (existingEncryptorDescriptor?.ImplementationInstance is IConfigEncryptor preRegistered)
        {
            // Consumer pre-registered a concrete instance — use it on both sides synchronously.
            // This is the v1.3 instance-registration path; behavior is unchanged.
            pollingEncryptor = preRegistered;
            needsActivator = false;
        }
        else if (existingEncryptorDescriptor is not null)
        {
            // Consumer registered via type-mapping (AddSingleton<IConfigEncryptor, MyImpl>())
            // or factory. We cannot resolve without BuildServiceProvider(), so the polling-side
            // store gets null (returns raw ciphertext). The DbConfigEncryptorActivator hosted
            // service will call provider.SetEncryptor(...) after the host is built.
            pollingEncryptor = null;
            needsActivator = true;
        }
        else
        {
            // No consumer registration — construct the default ephemeral encryptor and register
            // it as a concrete singleton so both the polling side (direct) and the HTTP side (DI)
            // use the same key ring.
            var dpProvider = DataProtectionProvider.Create("DbConfig");
            pollingEncryptor = new DataProtectionConfigEncryptor(dpProvider);
            hostBuilder.Services.AddSingleton<IConfigEncryptor>(pollingEncryptor);
            needsActivator = false;
        }

        // ---- Register HTTP/DI-side stack into host DI ----
        hostBuilder.Services.AddSingleton(options);
        hostBuilder.Services.TryAddSingleton(TimeProvider.System);
        hostBuilder.Services.TryAddSingleton(detector);
        hostBuilder.Services.AddDbContextFactory<DbConfigDbContext>(contextConfig);

        // Resolve EfCoreConfigStore via a factory so we pass DbConfigOptions and (optionally)
        // ITenantResolver into the convenience-aware constructor. Both are needed for the
        // implicit-app/env overloads (GetAsync(key), GetAsync<T>(), GetForTenantAsync<T>(),
        // etc.) added in v0.11.1.
        hostBuilder.Services.TryAddSingleton<IConfigStore>(sp =>
        {
            return new EfCoreConfigStore(
                sp.GetRequiredService<IDbContextFactory<DbConfigDbContext>>(),
                sp.GetRequiredService<IUniqueConstraintDetector>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<DbConfigOptions>(),
                sp.GetService<IConfigEncryptor>(),
                enableAuditLog: true,
                tenantResolver: sp.GetService<ITenantResolver>());
        });

        // Marker + reload signal — pure object plumbing.
        var marker = new DbConfigRegistrationMarker(dbb);
        hostBuilder.Services.AddSingleton(marker);
        hostBuilder.Services.AddSingleton<IDbConfigReloadSignal>(sp =>
        {
            var m = sp.GetRequiredService<DbConfigRegistrationMarker>();
            return m.Source?.Provider
                ?? throw new InvalidOperationException(
                    "IDbConfigReloadSignal cannot be resolved before host construction has built the configuration system.");
        });

        // Tenant config reader — exposes typed binding scoped to a specific tenant id.
        // The polling provider implements the AsyncLocal override that the reader uses to
        // pin the tenant for the duration of an IOptionsSnapshot<T> resolution.
        hostBuilder.Services.AddSingleton<ITenantConfigReader>(sp =>
        {
            var m = sp.GetRequiredService<DbConfigRegistrationMarker>();
            var provider = m.Source?.Provider
                ?? throw new InvalidOperationException(
                    "ITenantConfigReader cannot be resolved before host construction has built the configuration system.");
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new TenantConfigReader(provider, scopeFactory);
        });

        // Always register the tenant activator so the provider gets access to the host DI
        // service provider after build. This enables lazy ITenantResolver resolution in TryGet.
        hostBuilder.Services.AddHostedService<DbConfigTenantActivator>();

        // Register the encryptor activator hosted service when a type-mapped/factory registration
        // is present. It resolves IConfigEncryptor from the host SP in StartAsync and injects it
        // into the polling-side provider so secret values can be decrypted post-build.
        if (needsActivator)
        {
            hostBuilder.Services.AddHostedService<DbConfigEncryptorActivator>();
        }

        // ---- Build the polling-side store directly, with NO DI lookup ----
        // We avoid BuildServiceProvider() by constructing the EF context factory manually.
        //
        // The polling-side store is constructed with NO encryptor (PassthroughConfigEncryptor
        // is used internally). This is deliberate: the store therefore returns raw stored
        // values (ciphertext for IsSecret rows) into the provider's _tenantData, and the
        // provider's TryGet path performs decryption via _encryptor. This makes the polling
        // pipeline uniform across all three registration shapes (instance, type-mapped,
        // default) — decryption always happens at the provider seam, never twice.
        //
        // The HTTP-side store (registered in DI above) still receives the real encryptor
        // via constructor injection and continues to decrypt at the store layer for API
        // responses.
        var pollingOptionsBuilder = new DbContextOptionsBuilder<DbConfigDbContext>();
        contextConfig(pollingOptionsBuilder);

        // ---- Auto-migrate if requested (default) ----
        // The polling provider's first Load() runs synchronously inside hostBuilder.Configuration.Add(source)
        // below and queries DbConfig_Entries. If the schema isn't applied yet the call throws.
        // SchemaMode.CreateIfMissing (default) applies any pending migrations now, before the source is
        // added. Production teams that prefer DBA/CI-pipeline schema management set SchemaMode.None.
        // Migrate() is synchronous by design — we're still in builder time, no host yet, and the operation
        // must complete before the source is added.
        if (options.SchemaMode == SchemaMode.CreateIfMissing)
        {
            using var migrateCtx = new DbConfigDbContext(pollingOptionsBuilder.Options);
            migrateCtx.Database.Migrate();
        }

        IDbContextFactory<DbConfigDbContext> pollingFactory =
            new DirectDbContextFactory(pollingOptionsBuilder.Options);

        // Polling-side store: pass DbConfigOptions so callers that touch the (internal) polling store
        // through diagnostic surfaces have access to the same convenience overloads. ITenantResolver
        // is null — the polling store is internal-use only; consumer code uses the HTTP-side
        // IConfigStore from DI, which has the resolver injected via the factory above.
        var pollingStore = new EfCoreConfigStore(
            pollingFactory,
            detector,
            TimeProvider.System,
            options,
            encryptor: null,
            enableAuditLog: true,
            tenantResolver: null);

        var source = new DbConfigConfigurationSource(options, pollingStore, TimeProvider.System, NullLoggerFactory.Instance);
        marker.SetSource(source);
        hostBuilder.Configuration.Add(source);

        // For the instance-registered and default-encryptor paths, inject the encryptor into
        // the polling provider synchronously now that ConfigurationManager.Add(source) has
        // called Build() (which constructed the provider) and Load() (which populated raw
        // ciphertext into the provider's _tenantData). This avoids the post-build activator
        // for these paths and preserves the v1.3 behavior of "secret reads work immediately".
        // The type-mapped path is unchanged: pollingEncryptor is null here, and the
        // DbConfigEncryptorActivator hosted service calls SetEncryptor in StartAsync.
        if (pollingEncryptor is not null)
        {
            source.Provider?.SetEncryptor(pollingEncryptor);
        }

        return hostBuilder;
    }

    /// <summary>
    /// Minimal <see cref="IDbContextFactory{TContext}"/> implementation that creates a new
    /// <see cref="DbConfigDbContext"/> per call using directly provided options.
    /// Used by the polling-side store so that no second DI root container is needed.
    /// </summary>
    private sealed class DirectDbContextFactory : IDbContextFactory<DbConfigDbContext>
    {
        private readonly DbContextOptions<DbConfigDbContext> _options;

        public DirectDbContextFactory(DbContextOptions<DbConfigDbContext> options)
        {
            _options = options;
        }

        public DbConfigDbContext CreateDbContext()
        {
            return new DbConfigDbContext(_options);
        }

        public async Task<DbConfigDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(new DbConfigDbContext(_options)).ConfigureAwait(false);
        }
    }
}
