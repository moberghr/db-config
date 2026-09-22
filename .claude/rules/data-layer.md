---
paths:
  - "src/core/DbConfig.EntityFrameworkCore/**"
  - "src/core/providers/**"
axes:
  decision: structure
  topic: data-layer
  scope: project
---

# Data Layer

> Scope: `DbConfig.EntityFrameworkCore` and the two provider packages. Verified against the
> codebase on 2026-09-21: 0 `.Include(`, 42 `.Select(`, 46 `AsNoTracking`, 0 EF raw-SQL API calls
> (`FromSqlRaw`/`ExecuteSqlRaw`) in `src/`. Raw SQL does exist, deliberately, in the provider
> migrators — see §5.1.

- **§5.1** No raw SQL in `DbConfig.Core` or `DbConfig.EntityFrameworkCore` — all queries use EF Core LINQ. `[ENFORCED]` (zero counter-examples in those two projects). **Exception:** provider-native APIs are allowed inside the provider packages, and that exception is actively used: `SqlServerDbConfigMigrator.cs` and `PostgreSqlDbConfigMigrator.cs` execute `Sql/InitialCreate.sql` through `DbCommand.CommandText` / `ExecuteNonQueryAsync`, and the unique-constraint detectors inspect `SqlException`/`PostgresException`. `DbConfig.Core` must not reference `Npgsql` or `Microsoft.Data.SqlClient`.
- **§5.2** `AsNoTracking()` on every read-only query. `[ENFORCED]` — the store's read paths (`GetAllAsync`, `GetAsync`, `GetLatestModifiedUtcAsync`, `GetHistoryAsync`) are all non-tracking.
- **§5.3** `.Select()` projections over `.Include()` for reads. `[ENFORCED]` — the codebase has zero `.Include(` calls. Only load full entities when updating (`UpsertAsync`/`DeleteAsync` fetch the tracked row deliberately).
- **§5.4** `DbConfigDbContext` lives in `Moberg.DbConfig.EntityFrameworkCore`, shared by both providers. `OnModelCreating` carries NO table-name literals — names come from the entity sets and the configured schema, so the entries table is `[ConfigEntries]` on SQL Server and `"config_entries"` on PostgreSQL (snake_case via `EFCore.NamingConventions`); likewise `AuditEntries` / `audit_entries`. Indexes (`DbConfigDbContext.cs:82,85,132`): unique on `(Scope, Environment, TenantId, Key)`, polling on `(Scope, Environment, TenantId, ModifiedUtc)`, audit history on `(Scope, Environment, TenantId, Key, ModifiedUtc)`. Never move it back into `Core` — that pulls `EntityFrameworkCore.Relational` onto every consumer, including those writing custom non-EF stores.
- **§5.5** Schema is configurable (v0.13.0+) rather than hard-coded; PostgreSQL applies `snake_case` naming via `EFCore.NamingConventions`. Set schema through options, not by re-pinning table names with `.ToTable()`.
- **§5.6** `TimeProvider` for all timestamps in production code. Never `DateTime.UtcNow` outside test code — the polling provider's tests advance time without wall-clock delays. Registered via `TryAddSingleton(TimeProvider.System)` in `AddDbConfig`.
- **§5.7** Mutation and its audit row commit in ONE `SaveChangesAsync` — see `architecture.md` §2.13 and AGENTS.md §0.5. Note `EfCoreConfigStore.UpsertAsync` wraps this in a two-attempt loop (`EfCoreConfigStore.cs:244,307`): on a detected unique-constraint violation the first attempt is re-read, merged, and saved again. That is last-writer-wins resolution, not a second uncoordinated write — each attempt still commits mutation + audit atomically.
- **§5.8** Use async EF Core methods (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`) and pass `CancellationToken` through — every `IConfigStore` method takes one.
- **§5.9** `GetAsync` MUST issue a single-row targeted query (`WHERE Key = @k`). Never call `GetAllAsync` and filter in memory for a single-key endpoint.
- **§5.10** There are NO EF Core migrations in this repo — no `Migrations/` folder, no `Designer.cs`, no `ModelSnapshot.cs`, no `MigrationsAssembly`. Schema is created by hand-written SQL (`src/core/providers/*/Sql/InitialCreate.sql`) executed by `SqlServerDbConfigMigrator` / `PostgreSqlDbConfigMigrator`. To change the schema, edit the entity, then edit BOTH SQL scripts. See `project-specific.md` §8.3.
