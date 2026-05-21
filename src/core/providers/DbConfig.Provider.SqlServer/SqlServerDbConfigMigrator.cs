using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DbConfig.Provider.SqlServer;

/// <summary>
/// Applies the DbConfig schema on SQL Server by executing the embedded idempotent
/// <c>InitialCreate.sql</c> script. No EF Core migrations involved.
/// </summary>
/// <remarks>
/// Idempotency: every statement in the script is guarded with <c>IF NOT EXISTS</c>.
/// Safe to call repeatedly. Schema is substituted via a <c>{schema}</c> placeholder.
/// </remarks>
public static class SqlServerDbConfigMigrator
{
    private const string ScriptResourceName = "DbConfig.Provider.SqlServer.Sql.InitialCreate.sql";
    private const string DefaultSchema = "configuration";

    // GO must be on its own line (ignoring whitespace). Splits the script into batches
    // that we can submit to SqlConnection separately — SqlConnection does not accept GO
    // as a T-SQL keyword.
    private static readonly Regex GoSplitter = new(
        @"^\s*GO\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    // Defense-in-depth identifier validation. The schema string is substituted into raw DDL
    // with no SQL escaping; rejecting anything that isn't a plain identifier closes the
    // injection vector and produces a clear error instead of a cryptic DB-side syntax error
    // for typos like "my schema" or trailing whitespace.
    private static readonly Regex LegalIdentifier = new(
        @"^[A-Za-z_][A-Za-z0-9_]{0,62}$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Applies the DbConfig schema to the SQL Server database identified by
    /// <paramref name="connectionString"/>. Idempotent — safe to call repeatedly.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="schema">Database schema for DbConfig tables. Defaults to
    /// <c>"configuration"</c>; pass <see langword="null"/> to use the database default (<c>dbo</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task MigrateAsync(
        string connectionString,
        string? schema = DefaultSchema,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        var sql = GetCreateScript(schema);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        foreach (var batch in SplitBatches(sql))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the full idempotent create-DDL script with <paramref name="schema"/>
    /// substituted. Pass <see langword="null"/> to target the database default schema (<c>dbo</c>).
    /// </summary>
    /// <remarks>
    /// The schema name is concatenated into raw DDL — it MUST be a plain SQL identifier
    /// (alphanumerics + underscore, leading non-digit, up to 63 characters). The method
    /// rejects anything else with <see cref="ArgumentException"/>. Schema names are
    /// host-author constants in the documented flow, not user input; this check is
    /// defense-in-depth.
    /// </remarks>
    public static string GetCreateScript(string? schema = DefaultSchema)
    {
        var effectiveSchema = string.IsNullOrEmpty(schema) ? "dbo" : schema;

        if (!LegalIdentifier.IsMatch(effectiveSchema))
        {
            throw new ArgumentException(
                $"Schema '{effectiveSchema}' is not a valid SQL identifier. " +
                "Expected: letters/digits/underscore, leading non-digit, up to 63 characters.",
                nameof(schema));
        }

        return LoadEmbeddedScript().Replace("{schema}", effectiveSchema, StringComparison.Ordinal);
    }

    private static string LoadEmbeddedScript()
    {
        var asm = typeof(SqlServerDbConfigMigrator).Assembly;
        using var stream = asm.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ScriptResourceName}' not found in {asm.FullName}. " +
                $"Available: [{string.Join(", ", asm.GetManifestResourceNames())}].");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> SplitBatches(string script)
    {
        foreach (var batch in GoSplitter.Split(script))
        {
            var trimmed = batch.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }
}
