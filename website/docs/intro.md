---
sidebar_position: 1
---

import Screenshot from '@site/src/components/Screenshot';

# Introduction

DbConfig is a database-backed `IConfiguration` provider for .NET 8. It lets you store
configuration values in your existing SQL Server or PostgreSQL database, edit them through
an embedded React UI, and reload them in running processes — no redeploy required.

It is **not** a hosted secrets manager like Azure Key Vault or AWS Secrets Manager. There
is no extra service to run. Everything lives in your app's own database, behind your own
auth policy.

<Screenshot light="/img/screenshots/01-entries-list.png" dark="/img/screenshots/01-entries-list-dark.png" alt="DbConfig entries list" />

## Quick example

Two lines of setup in `Program.cs`:

```csharp
// 1. Single call — wires services, configuration source, reload signal, and
//    auto-migrates the DbConfig schema (SchemaMode.CreateIfMissing is the default).
builder.AddDbConfig(b =>
{
    b.Options.AppName = "MyApp";
    b.Options.Environment = builder.Environment.EnvironmentName;
    b.Options.ReloadInterval = TimeSpan.FromSeconds(30);
    b.UseSqlServer(connectionString); // or b.UsePostgreSql(connectionString)
});

// 2. Register your IDbConfigCredentialValidator, then mount UI + HTTP API
//    under one prefix with a shared signed cookie.
builder.Services.AddScoped<IDbConfigCredentialValidator, MyValidator>();
var app = builder.Build();

app.MapDbConfigAdmin("/admin/dbconfig", opts =>
    opts.UseBuiltInLogin<MyValidator>());
// UI at  /admin/dbconfig
// API at /admin/dbconfig/api
```

After that, `IConfiguration["MyKey"]` reads from the database. Any change made via the
UI or the HTTP API fires a reload that reaches every `IOptionsMonitor<T>` subscriber in the
same process within milliseconds.

## What you get

- **Per-entry encryption.** Mark an entry `IsSecret = true` and its value is encrypted at
  rest using ASP.NET Core Data Protection (or your own `IConfigEncryptor`). Non-secret
  entries stay plaintext for easy inspection with SSMS or psql.
- **Audit log.** Every mutation (insert, update, delete) writes a row to
  `DbConfig_AuditEntries` in the same database transaction. Opt-in read auditing is
  available for compliance scenarios.
- **Multi-scope configuration.** Pull values from one or more shared scopes (e.g.
  `PlatformDefaults`, `Shared`) in addition to your app's own scope, with explicit
  precedence ordering.
- **Embedded React UI.** One call (`MapDbConfigAdmin`) mounts a full-featured editor —
  CRUD, secret masking, scope badges, bulk operations, import/export, a per-row audit
  history dialog, and a global Audit Log tab.
- **Composable authorization.** Defaults are open access; opt into the built-in cookie
  login via `IDbConfigCredentialValidator` and `opts.UseBuiltInLogin<T>()`, plug in a
  custom `IDbConfigAuthorizationFilter`, or fall back to your host's existing
  `RequireAuthorization(...)` pipeline.
- **Auto-migrating schema.** `SchemaMode.CreateIfMissing` (default) applies the EF Core
  migrations on startup, matching Hangfire / Marten / Wolverine conventions. Production
  teams that prefer DBA-controlled schema use `SchemaMode.None` and the
  `DbConfigMigrator.GenerateMigrationScript` helper.

## What you bring

- **A SQL Server or PostgreSQL database** that your application already has a connection
  string for. DbConfig creates its own tables (`DbConfig_Entries`,
  `DbConfig_AuditEntries`) via EF Core migrations; it does not touch your schema. By
  default the schema is applied automatically on startup — see
  [Migrations](./operations/migrations.md).
- **An ASP.NET Core 8 host.** `AddDbConfig` is an extension on `IHostApplicationBuilder`,
  so it also works in worker services and generic hosts — but `MapDbConfigAdmin` (and
  its split-form siblings `MapDbConfigHttp` / `MapDbConfigUi`) require the ASP.NET Core
  middleware pipeline.
- **An auth strategy for the admin surface.** Pick one: open access (private network or
  dev), built-in cookie login via `IDbConfigCredentialValidator`, a custom
  `IDbConfigAuthorizationFilter`, or compose with your existing identity pipeline (JWT,
  Azure AD, Windows Auth) via `RequireAuthorization(...)`. See
  [Authentication & authorization](./configuration/auth.md).

## Where to next

| Goal | Page |
|------|------|
| Add NuGet packages and create the tables | [Installation](./getting-started/installation.md) |
| Register DbConfig in an existing app | [Single-call DI](./configuration/single-call-di.md) |
| Understand the editor UI | [UI Editor overview](./ui-editor/overview.md) |
| See a full working example | [Demo host](./getting-started/demo-host.md) |
| Learn about encryption and key persistence | [Encryption](./configuration/encryption.md) |
