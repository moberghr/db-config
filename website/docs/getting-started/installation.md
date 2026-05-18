---
sidebar_position: 1
---

# Installation

## NuGet packages

DbConfig is split across five packages. Install the ones you need:

| Package | Purpose |
|---------|---------|
| `Moberg.DbConfig.Core` | `IConfigurationSource`, `IConfigurationProvider`, `IConfigStore`, `DbConfigOptions` |
| `Moberg.DbConfig.EntityFrameworkCore` | EF Core `DbContext`, `EfCoreConfigStore`, the `AddDbConfig` entry point |
| `Moberg.DbConfig.Http` | JSON API endpoints (`MapDbConfigHttp`) |
| `Moberg.DbConfig.Ui` | Embedded React editor UI (`MapDbConfigUi`) |
| `Moberg.DbConfig.Provider.SqlServer` | SQL Server provider + migrations |
| `Moberg.DbConfig.Provider.PostgreSql` | PostgreSQL (Npgsql) provider + migrations |

A typical web application installing the full stack on SQL Server:

```bash
dotnet add package Moberg.DbConfig.EntityFrameworkCore
dotnet add package Moberg.DbConfig.Http
dotnet add package Moberg.DbConfig.Ui
dotnet add package Moberg.DbConfig.Provider.SqlServer
```

For PostgreSQL, replace the last line:

```bash
dotnet add package Moberg.DbConfig.Provider.PostgreSql
```

`Moberg.DbConfig.Core` is pulled in transitively by the other packages. You only need to
reference it directly if you are writing a custom `IConfigStore` implementation.

### Required framework versions

- **.NET 8** (LTS) — `net8.0` TFM required
- **EF Core 8.0.11** — pinned by the provider packages; do not downgrade

### Optional: persistent Data Protection keys

The default Data Protection key ring is ephemeral. Encrypted values become unreadable after
a process restart. If you use `IsSecret = true`, configure key persistence before your first
deployment:

```bash
dotnet add package Microsoft.AspNetCore.DataProtection.AzureStorage
# or: Microsoft.AspNetCore.DataProtection.FileSystem (for single-instance)
```

See [Key persistence](../operations/key-persistence.md) for the full setup.

## First-time database setup

DbConfig manages its own schema through EF Core migrations shipped inside the provider
packages. You need to apply these migrations once before starting the application.

### Option A — CLI (recommended for CI/CD)

```bash
dotnet ef database update \
  --project src/YourApp/YourApp.csproj \
  --startup-project src/YourApp/YourApp.csproj \
  --context DbConfigDbContext
```

The `DbConfigDbContext` is registered in DI by `AddDbConfig`. The CLI reads it from the
startup project's service provider.

### Option B — Programmatic (useful for demos and dev environments)

Call `MigrateAsync` at startup before the host runs. This is what the demo app does:

```csharp
// In Program.cs, after builder.Build()
var migrateOptions = new DbContextOptionsBuilder<DbConfigDbContext>()
    .UseSqlServer(
        connectionString,
        sql => sql.MigrationsAssembly("DbConfig.Provider.SqlServer"))
    .Options;

await using var ctx = new DbConfigDbContext(migrateOptions);
await ctx.Database.MigrateAsync();
```

For PostgreSQL, replace `UseSqlServer` with `UseNpgsql` and the assembly name with
`"DbConfig.Provider.PostgreSql"`.

:::warning
Applying migrations programmatically on every startup is convenient for development but
can cause schema-lock contention in multi-instance production deployments. Prefer the CLI
in production or use a dedicated migration job.
:::

### What the migrations create

Three migrations ship with each provider:

| Migration | What it does |
|-----------|-------------|
| `InitialCreate` | Creates `DbConfig_Entries` table with the composite unique constraint and polling index |
| `AddAuditEntries` | Creates `DbConfig_AuditEntries` table for the audit log |
| `CaseSensitiveScopeColumns` | Sets binary collation on `AppName`, `Environment`, and `Key` columns in both tables |

You do not need to run `dotnet ef migrations add` yourself. The migrations are embedded in
the provider assembly. See [Migrations](../operations/migrations.md) for the full runbook.

### MigrationsAssembly note

The `DbConfigDbContext` is in `Moberg.DbConfig.EntityFrameworkCore`, but the migrations
live in the provider assembly. The provider registers this via:

```csharp
options.MigrationsAssembly("DbConfig.Provider.SqlServer")
// or
options.MigrationsAssembly("DbConfig.Provider.PostgreSql")
```

This is handled internally by `UseSqlServer`/`UsePostgreSql`. You do not need to configure
it yourself.

## Next step

Once the packages are installed and the database is migrated, register DbConfig in your
host:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
});
```

See [Single-call DI](../configuration/single-call-di.md) for all available options, or
jump straight to [First config](./first-config.md) for a step-by-step walkthrough.
