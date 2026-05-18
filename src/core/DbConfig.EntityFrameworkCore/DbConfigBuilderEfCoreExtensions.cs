using DbConfig.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DbConfig.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="DbConfigBuilder"/> for EF Core integration.
/// Provider packages delegate to <see cref="UseEntityFrameworkCore"/> and
/// configure the database-specific connection (e.g. SQL Server, PostgreSQL).
/// </summary>
public static class DbConfigBuilderEfCoreExtensions
{
    /// <summary>
    /// Captures the EF Core context configuration action on the builder for later use
    /// by <see cref="HostApplicationBuilderExtensions.AddDbConfig"/>.
    /// <para>
    /// Provider packages must call <see cref="DbConfigBuilder.SetDetector"/> separately
    /// before calling this method. Do not call this extension directly — call your provider's
    /// <c>UseSqlServer</c> or <c>UsePostgreSql</c> instead.
    /// </para>
    /// <para>
    /// <see cref="IConfigEncryptor"/> registration is handled by
    /// <see cref="HostApplicationBuilderExtensions.AddDbConfig"/> after the user's configure
    /// lambda runs. This ensures the same encryptor instance is shared by both the polling-side
    /// and the HTTP/DI-side stores. Do not register <see cref="IConfigEncryptor"/> here.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if called more than once on the same builder.</exception>
    public static DbConfigBuilder UseEntityFrameworkCore(
        this DbConfigBuilder builder,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        // Delegates to the internal capture method; double-call guard lives there.
        builder.SetConfigureDbContext(configureDbContext);

        // Ensure Data Protection services are present (internally TryAdd-based so safe to call repeatedly).
        builder.Services.AddDataProtection();

        // Register the EF Core audit store. TryAddSingleton so consumers can override.
        builder.Services.TryAddSingleton<IConfigAuditStore, EfCoreConfigAuditStore>();

        return builder;
    }
}
