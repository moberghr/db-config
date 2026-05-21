using DbConfig.Provider.PostgreSql;
using DbConfig.Provider.SqlServer;
using DbConfig.Tests.TestData;
using Shouldly;

namespace DbConfig.Tests.EntityFrameworkCore;

/// <summary>
/// Pure-unit tests for the per-provider migrators' schema-name validation. These tests
/// touch only the embedded SQL script and a regex; no database, no Docker container, no
/// host build. Lives outside the <c>SqlServerCustomSchemaTests</c> / <c>PostgreSqlCustomSchemaTests</c>
/// fixtures because those start containers and would force a ~5 s budget for what should
/// take milliseconds.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DbConfigMigratorValidationTests
{
    [TimedFact]
    public void PostgreSql_GetCreateScript_PascalCaseSchema_ThrowsArgumentException()
    {
        // PG runtime model applies UseSnakeCaseNamingConvention. If the migrator accepted a
        // PascalCase schema string it would create "AppConfig"."config_entries" on disk while
        // EF runtime queries would target "app_config"."config_entries" — a runtime
        // missing-relation error. The migrator rejects the input up-front instead.
        var ex = Should.Throw<ArgumentException>(
            () => PostgreSqlDbConfigMigrator.GetCreateScript(schema: "AppConfig"));

        ex.Message.ShouldContain("snake-case", Case.Insensitive);
    }

    [TimedFact]
    public void PostgreSql_GetCreateScript_InvalidIdentifier_ThrowsArgumentException()
    {
        // Defense-in-depth: schema is concatenated into raw DDL, so non-identifier characters
        // must be rejected. Covers the SQL-injection vector even though schema is normally a
        // host-author constant.
        Should.Throw<ArgumentException>(
            () => PostgreSqlDbConfigMigrator.GetCreateScript(schema: "my schema"));
        Should.Throw<ArgumentException>(
            () => PostgreSqlDbConfigMigrator.GetCreateScript(schema: "schema]; drop table users; --"));
    }

    [TimedFact]
    public void SqlServer_GetCreateScript_InvalidIdentifier_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(
            () => SqlServerDbConfigMigrator.GetCreateScript(schema: "my schema"));
        Should.Throw<ArgumentException>(
            () => SqlServerDbConfigMigrator.GetCreateScript(schema: "schema]; DROP TABLE users; --"));
    }

    [TimedFact]
    public void PostgreSql_GetCreateScript_LegalSnakeSchema_Succeeds()
    {
        // Sanity: the validator accepts well-formed snake-case identifiers.
        var sql = PostgreSqlDbConfigMigrator.GetCreateScript(schema: "app_config");

        sql.ShouldContain("app_config");
        sql.ShouldNotContain("{schema}");
    }

    [TimedFact]
    public void SqlServer_GetCreateScript_LegalIdentifier_Succeeds()
    {
        // Sanity: SQL Server validator accepts mixed-case identifiers (no snake-case requirement).
        var sql = SqlServerDbConfigMigrator.GetCreateScript(schema: "AppConfig");

        sql.ShouldContain("AppConfig");
        sql.ShouldNotContain("{schema}");
    }
}
