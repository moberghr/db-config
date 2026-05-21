using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace DbConfig.Tests.PostgreSql;

[CollectionDefinition("PostgreSql")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>;

[Trait("Category", "PostgreSql")]
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public const string CollectionName = "PostgreSql";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
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

        // Same setup as production via PostgreSqlDbConfigOptions.ForPostgreSql — snake_case
        // naming convention, the configuration schema, the custom IMigrationsAssembly, and the
        // migrations-assembly name. Inlined here because AddDbContextFactory takes an Action,
        // not a pre-built DbContextOptions instance.
        var services = new ServiceCollection();
        services.AddDbContextFactory<DbConfigDbContext>(options =>
        {
            options.UseNpgsql(ConnectionString, npg => npg.MigrationsAssembly("DbConfig.Provider.PostgreSql"));
            options.UseSnakeCaseNamingConvention();
            options.UseDbConfigSchema("configuration");
            options.ReplaceService<IMigrationsAssembly, DbConfigMigrationsAssembly>();
        });

        _serviceProvider = services.BuildServiceProvider();
        DbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<DbConfigDbContext>>();

        await using var context = await DbContextFactory.CreateDbContextAsync(ct);
        await context.Database.MigrateAsync(ct);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        _respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["configuration"],
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
        await using var connection = new NpgsqlConnection(ConnectionString);
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
