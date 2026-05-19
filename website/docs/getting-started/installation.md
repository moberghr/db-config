---
sidebar_position: 1
---

# Installation

## NuGet packages

DbConfig is split across six packages. Install the ones you need:

| Package | Purpose |
|---------|---------|
| `Moberg.DbConfig.Core` | `IConfigurationSource`, `IConfigurationProvider`, `IConfigStore`, `DbConfigOptions`, `SchemaMode` |
| `Moberg.DbConfig.EntityFrameworkCore` | EF Core `DbContext`, `EfCoreConfigStore`, `DbConfigMigrator`, the `AddDbConfig` entry point |
| `Moberg.DbConfig.Http` | JSON API endpoints, `IDbConfigCredentialValidator`, `IDbConfigAuthorizationFilter` (`MapDbConfigHttp`) |
| `Moberg.DbConfig.Ui` | Embedded React editor UI + built-in cookie login (`MapDbConfigUi`, `MapDbConfigAdmin`) |
| `Moberg.DbConfig.Provider.SqlServer` | SQL Server provider + migrations + `SqlServerDbConfigOptions.ForSqlServer` helper |
| `Moberg.DbConfig.Provider.PostgreSql` | PostgreSQL (Npgsql) provider + migrations + `PostgreSqlDbConfigOptions.ForPostgreSql` helper |

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
packages. **In v0.10.2+, the schema is applied automatically on host startup** — you do
not need to run `dotnet ef database update` manually.

### Default: auto-migrate at startup (`SchemaMode.CreateIfMissing`)

`AddDbConfig` invokes `Database.MigrateAsync` synchronously before the polling provider's
first `Load()`. This is the default behaviour and matches Hangfire / Marten / Wolverine
conventions:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
    // b.Options.SchemaMode = SchemaMode.CreateIfMissing; // already the default
});
```

EF Core's `__EFMigrationsHistory` tracks state, so the call is idempotent — pending
migrations apply on first run; subsequent runs are no-ops.

### Production opt-out: DBA-controlled schema (`SchemaMode.None`)

Set `SchemaMode.None` to skip the startup migration entirely. Use this when DBAs or a
CI/CD pipeline owns the schema and applies it out of band:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.SchemaMode = SchemaMode.None;
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
});
```

Generate the SQL for offline application via `DbConfigMigrator`:

```csharp
var opts = SqlServerDbConfigOptions.ForSqlServer(connectionString);
var sql = DbConfigMigrator.GenerateMigrationScript(opts, idempotent: true);
File.WriteAllText("dbconfig-upgrade.sql", sql);
```

For PostgreSQL, swap `SqlServerDbConfigOptions.ForSqlServer` for
`PostgreSqlDbConfigOptions.ForPostgreSql`. Both helpers hide the `MigrationsAssembly`
magic string.

See [Migrations](../operations/migrations.md) for the full DBA runbook and CI/CD
patterns.

### What the migrations create

Three migrations ship with each provider:

| Migration | What it does |
|-----------|-------------|
| `InitialCreate` | Creates `DbConfig_Entries` table with the composite unique constraint and polling index |
| `AddAuditEntries` | Creates `DbConfig_AuditEntries` table for the audit log |
| `CaseSensitiveScopeColumns` | Sets binary collation on `AppName`, `Environment`, `TenantId`, and `Key` columns in both tables |

You do not need to run `dotnet ef migrations add` yourself. The migrations are embedded in
the provider assembly. See [Migrations](../operations/migrations.md) for the full runbook
including the multi-instance startup race caveat.

## Next step

Once the packages are installed:

```csharp
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.UseSqlServer(connectionString);
});

builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();
var app = builder.Build();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<MyValidator>());
```

See [Single-call DI](../configuration/single-call-di.md) for all available options, or
jump straight to [First config](./first-config.md) for a step-by-step walkthrough.
