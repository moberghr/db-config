using DbConfig.Core;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DbConfig.Tests.Core;

[Trait("Category", "Unit")]
public sealed class AddDbConfigContractTests
{
    [TimedFact]
    public void AddDbConfig_WithoutProviderExtension_ThrowsInvalidOperationException()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Empty configure lambda — no UseSqlServer / UsePostgreSql call.
        var exception = Should.Throw<InvalidOperationException>(
            () => builder.AddDbConfig(b =>
            {
                b.Options.AppName = "App";
                b.Options.Environment = "Test";
            }));

        // Message must identify the missing provider call.
        exception.Message.ToLowerInvariant().ShouldContain("provider");
    }

    [TimedFact]
    public void AddDbConfig_CalledTwiceOnSameHost_ThrowsInvalidOperationException()
    {
        const string connectionString = "Server=127.0.0.1,19999;Database=test;User Id=sa;Password=fake;Connect Timeout=1;Encrypt=false;";
        var builder = WebApplication.CreateSlimBuilder();

        // First call: with WebApplicationBuilder (ConfigurationManager), Configuration.Add
        // triggers Load() which fails for a non-existent DB. Catch that and move on.
        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.AppName = "App";
                b.Options.Environment = "Test";
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(connectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() fails because the fake SQL Server is unreachable.
        }

        // Second call on the same host must throw the double-registration guard.
        var exception = Should.Throw<InvalidOperationException>(
            () => builder.AddDbConfig(b =>
            {
                b.Options.AppName = "App2";
                b.Options.Environment = "Test";
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(connectionString);
            }));

        exception.Message.ShouldContain("already been called");
    }

    [TimedFact]
    public void AddDbConfig_RegistersExpectedServices_InHostServiceCollection()
    {
        const string connectionString = "Server=127.0.0.1,19999;Database=test;User Id=sa;Password=fake;Connect Timeout=1;Encrypt=false;";
        var builder = WebApplication.CreateSlimBuilder();

        // With WebApplicationBuilder, Configuration.Add triggers Load() immediately.
        // For a fake connection string this throws. Services are registered before that.
        try
        {
            builder.AddDbConfig(b =>
            {
                b.Options.AppName = "App";
                b.Options.Environment = "Test";
                b.Options.SchemaMode = SchemaMode.None;
                b.UseSqlServer(connectionString);
            });
        }
        catch (InvalidOperationException)
        {
            // Expected: Load() fails because the fake SQL Server is unreachable.
        }

        // IConfigStore, IDbConfigReloadSignal, and marker must be registered.
        builder.Services.Any(x => x.ServiceType.Equals(typeof(IConfigStore))).ShouldBeTrue();
        builder.Services.Any(x => x.ServiceType.Equals(typeof(IDbConfigReloadSignal))).ShouldBeTrue();
        builder.Services.Any(x => x.ServiceType.Equals(typeof(DbConfigRegistrationMarker))).ShouldBeTrue();
    }
}
