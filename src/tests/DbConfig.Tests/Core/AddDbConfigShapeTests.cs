using DbConfig.Core;
using DbConfig.EntityFrameworkCore;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class AddDbConfigShapeTests
{
    private const string App = "ShapeTestApp";
    private const string Env = "Test";

    // A valid-looking (but non-functional) SQL Server connection string for unit tests
    // that don't actually connect to a database.
    // Connection string that fails fast — SQL auth to a non-existent local DB with 1-second timeout.
    private const string FakeConnectionString = "Server=127.0.0.1,19999;Database=test;User Id=sa;Password=fake;Connect Timeout=1;Encrypt=false;";

    [TimedFact]
    public void SingleCall_RegistersSourceAndServices_HappyPath()
    {
        // With WebApplicationBuilder (ConfigurationManager), adding the source eagerly
        // triggers Load(), which fails for a non-existent DB. We verify that services
        // ARE registered prior to the load attempt, and that the marker Source is set.
        var builder = WebApplication.CreateSlimBuilder();

        // AddDbConfig registers services then calls Configuration.Add, which triggers Load.
        // With a fake connection string that call throws. We accept that and inspect state.
        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;
                b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() throws because fake SQL Server is unreachable.
        }

        // Services are registered before Configuration.Add fires, so they are present.
        builder.Services.Any(x => x.ServiceType.Equals(typeof(IConfigStore))).ShouldBeTrue();
        builder.Services.Any(x => x.ServiceType.Equals(typeof(IDbConfigReloadSignal))).ShouldBeTrue();
        builder.Services.Any(x => x.ServiceType.Equals(typeof(DbConfigRegistrationMarker))).ShouldBeTrue();
        builder.Services.Any(x => x.ServiceType.Equals(typeof(DbConfigOptions))).ShouldBeTrue();
    }

    [TimedFact]
    public void SecondCall_OnSameHost_ThrowsInvalidOperationException()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // First call: catch the DB load error (fake connection).
        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() throws because fake SQL Server is unreachable.
        }

        // Second call must throw the "already been called" guard, not the DB error.
        var exception = Should.Throw<InvalidOperationException>(
            () => builder.AddDbConfig(b =>
            {
                b.Options.Scope = "App2";
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            }));

        exception.Message.ShouldContain("already been called");
    }

    [TimedFact]
    public void IDbConfigReloadSignal_IsResolvable_AfterAddDbConfig()
    {
        // In the single-call design, Source and Source.Provider are both set during
        // AddDbConfig (before the configuration source is added to the builder).
        // Even if Load() throws (e.g. DB unreachable), the signal can be resolved.
        var builder = WebApplication.CreateSlimBuilder();

        // AddDbConfig registers services and sets Source/Provider, then Configuration.Add
        // triggers Load() which throws for a fake connection. Catch that.
        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(FakeConnectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() throws because fake SQL Server is unreachable.
            // Source and Source.Provider are set before Load() is called.
        }

        // Build a DI container from the registered services.
        var sp = builder.Services.BuildServiceProvider();

        // IDbConfigReloadSignal must be resolvable because Source.Provider was set
        // during Build() before Load() was called.
        var signal = sp.GetRequiredService<IDbConfigReloadSignal>();
        signal.ShouldNotBeNull();
    }

    [TimedFact]
    public void LambdaWithoutUseProvider_ThrowsInvalidOperationException()
    {
        var builder = WebApplication.CreateSlimBuilder();

        var exception = Should.Throw<InvalidOperationException>(
            () => builder.AddDbConfig(b =>
            {
                b.Options.Scope = App;
                b.Options.Environment = Env;

                // No UseSqlServer / UsePostgreSql call.
            }));

        exception.Message.ToLowerInvariant().ShouldContain("provider");
    }
}
