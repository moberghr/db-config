using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Http;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;

namespace DbConfig.Tests.E2E;

[CollectionDefinition("E2E")]
public sealed class E2ECollection : ICollectionFixture<EndToEndFixture>;

[Trait("Category", "E2E")]
[Trait("Category", "SqlServer")]
public sealed class EndToEndFixture : IAsyncLifetime
{
    public const string CollectionName = "E2E";
    public const string AppName = "E2E";
    public const string EnvName = "Test";

    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _app!.Services;

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        await _container.StartAsync(ct);

        var connectionString = _container.GetConnectionString();

        // Apply EF migrations before starting the host.
        var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
            .Options;

        await using (var ctx = new DbConfigDbContext(migrateOptions))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        // Build the WebApplication with the real SQL Server-backed DbConfig provider.
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        builder.AddDbConfig(b =>
        {
            b.Options.AppName = AppName;
            b.Options.Environment = EnvName;
            b.Options.ReloadInterval = TimeSpan.FromMilliseconds(200);
            b.UseSqlServer(connectionString);
        });

        _app = builder.Build();
        _app.MapDbConfigHttp("/api/dbconfig");

        await _app.StartAsync(ct);

        Client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// Polls <paramref name="condition"/> up to <paramref name="timeout"/>,
    /// checking every 50 ms. Returns true when the condition becomes true,
    /// false if the timeout elapses.
    /// </summary>
    public static async Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (condition())
            {
                return true;
            }

            try
            {
                await Task.Delay(50, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return condition();
    }
}
