using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Testcontainers.MsSql;

namespace DbConfig.Tests.SqlServer;

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;

[Trait("Category", "SqlServer")]
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string CollectionName = "SqlServer";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner? _respawner;
    private ServiceProvider? _serviceProvider;

    public IDbContextFactory<DbConfigDbContext> DbContextFactory { get; private set; } = null!;

    /// <summary>
    /// A deterministic (ephemeral, in-memory key ring) encryptor shared across all tests
    /// within this fixture. Use when constructing an <see cref="EfCoreConfigStore"/> that
    /// needs to exercise the encryption/decryption path.
    /// </summary>
    public IConfigEncryptor Encryptor { get; } =
        new DataProtectionConfigEncryptor(DataProtectionProvider.Create("DbConfig.Tests"));

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await _container.StartAsync(ct);

        ConnectionString = _container.GetConnectionString();

        var services = new ServiceCollection();
        services.AddDbContextFactory<DbConfigDbContext>(options =>
            options.UseSqlServer(
                ConnectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer")));

        _serviceProvider = services.BuildServiceProvider();
        DbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<DbConfigDbContext>>();

        await using var context = await DbContextFactory.CreateDbContextAsync(ct);
        await context.Database.MigrateAsync(ct);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        _respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                TablesToIgnore = ["__EFMigrationsHistory"],
            });
    }

    public async Task ResetAsync()
    {
        if (_respawner is null)
        {
            return;
        }

        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await _respawner.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
