# Data Layer (§5)

> Cite rules as §5.N. EF Core 8. See `.claude/references/dotnet/ef-core-checklist.md`.

- **§5.1** `[ENFORCED]` `DbConfigDbContext.OnModelCreating` MUST NOT contain table/column name literals (`ToTable`/`HasColumnName`). Identifiers come from EF defaults; per-provider casing is applied by the provider pipeline (PostgreSQL `UseSnakeCaseNamingConvention` via EFCore.NamingConventions; SQL Server keeps PascalCase). Literals defeat the rewriter and produce queries targeting the wrong identifiers on PostgreSQL. Evidence: `DbConfigDbContext.cs`.
- **§5.2** `[CONVENTION]` Read queries use `AsNoTracking()` — all existing reads do (`EfCoreConfigStore.cs`, `EfCoreConfigAuditStore.cs`). Add it to new reads.
- **§5.3** `[CONVENTION]` Async EF methods (`ToListAsync`/`FirstOrDefaultAsync`) with a propagated `CancellationToken`. Keep filtering in the database.
- **§5.4** `[CONVENTION]` No raw/interpolated SQL in production paths (`FromSqlRaw`/`ExecuteSqlRaw` → 0 hits in src). Schema bootstrap ships as static per-provider SQL files (`src/core/providers/DbConfig.Provider.PostgreSql/Sql/InitialCreate.sql`, `src/core/providers/DbConfig.Provider.SqlServer/Sql/InitialCreate.sql`); if you must run raw SQL, keep it parameterized and out of hot read paths.
- **§5.5** `[CONVENTION]` Entity identity is the composite `(Scope, Environment, TenantId, Key)` unique index; `ModifiedUtc` round-trips via `UtcDateTimeOffsetConverter`. Preserve these when adding columns.
- **§5.6** `[CONVENTION]` Multi-tenancy is a first-class `TenantId` column; tenant reads support optional fallback to non-tenant values. Don't add tenant filtering ad-hoc — use `ITenantConfigReader`/`ITenantResolver`.
- **§5.7** `[CONVENTION]` Package versions are declared inline per `.csproj` (no central `Directory.Packages.props`). Keep EF/provider versions aligned at the `8.0.x` line.
