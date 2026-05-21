using System.Text.RegularExpressions;
using Npgsql;

namespace DbConfig.Provider.PostgreSql;

/// <summary>
/// Applies the DbConfig schema on PostgreSQL by executing the embedded idempotent
/// <c>InitialCreate.sql</c> script. No EF Core migrations involved.
/// </summary>
/// <remarks>
/// Idempotency: every statement uses <c>IF NOT EXISTS</c>. Safe to call repeatedly.
/// Schema is substituted via a <c>{schema}</c> placeholder.
/// </remarks>
public static class PostgreSqlDbConfigMigrator
{
    private const string ScriptResourceName = "DbConfig.Provider.PostgreSql.Sql.InitialCreate.sql";
    private const string DefaultSchema = "configuration";

    // PG schemas must be plain snake-case identifiers. We enforce a stricter pattern than
    // the SQL Server migrator because the EF runtime model on PG uses
    // UseSnakeCaseNamingConvention, which would rewrite a PascalCase schema to lower_case
    // before issuing queries — but the migrator substitutes the original casing into the
    // CREATE SCHEMA / CREATE TABLE DDL. Mismatched casing means the migrator creates
    // "MySchema" and runtime queries hit "my_schema" → missing-relation error. Rejecting
    // non-snake names up-front turns that runtime crash into a clear ArgumentException.
    private static readonly Regex LegalSnakeIdentifier = new(
        @"^[a-z_][a-z0-9_]{0,62}$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Applies the DbConfig schema to the PostgreSQL database identified by
    /// <paramref name="connectionString"/>. Idempotent — safe to call repeatedly.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="schema">Database schema for DbConfig tables. Defaults to
    /// <c>"configuration"</c>; pass <see langword="null"/> to use the database default (<c>public</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task MigrateAsync(
        string connectionString,
        string? schema = DefaultSchema,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var sql = GetCreateScript(schema);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the full idempotent create-DDL script with <paramref name="schema"/>
    /// substituted. Pass <see langword="null"/> to target the database default schema (<c>public</c>).
    /// </summary>
    /// <remarks>
    /// The schema name MUST be a snake-case identifier (lowercase letters/digits/underscore,
    /// leading non-digit). PG's runtime model uses <c>UseSnakeCaseNamingConvention</c>; a
    /// PascalCase schema would be rewritten by the convention but not by the migrator,
    /// causing a runtime missing-relation error. Anything that doesn't match the pattern
    /// throws <see cref="ArgumentException"/>.
    /// </remarks>
    public static string GetCreateScript(string? schema = DefaultSchema)
    {
        var effectiveSchema = string.IsNullOrEmpty(schema) ? "public" : schema;

        if (!LegalSnakeIdentifier.IsMatch(effectiveSchema))
        {
            throw new ArgumentException(
                $"Schema '{effectiveSchema}' is not a valid PostgreSQL snake-case identifier. " +
                "Expected: lowercase letters/digits/underscore, leading non-digit, up to 63 characters. " +
                "PG's snake_case convention would rewrite a non-snake schema name at runtime, " +
                "causing the runtime model to disagree with the on-disk schema.",
                nameof(schema));
        }

        return LoadEmbeddedScript().Replace("{schema}", effectiveSchema, StringComparison.Ordinal);
    }

    private static string LoadEmbeddedScript()
    {
        var asm = typeof(PostgreSqlDbConfigMigrator).Assembly;
        using var stream = asm.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ScriptResourceName}' not found in {asm.FullName}. " +
                $"Available: [{string.Join(", ", asm.GetManifestResourceNames())}].");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
